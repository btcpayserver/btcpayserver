using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.PayoutProcessors.Settings;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using PayoutData = BTCPayServer.Data.PayoutData;
using PayoutProcessorData = BTCPayServer.Data.Data.PayoutProcessorData;

namespace BTCPayServer.PayoutProcessors.OnChain
{
    public class OnChainAutomatedPayoutProcessor : BaseAutomatedPayoutProcessor<AutomatedPayoutBlob>
    {
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly BTCPayWalletProvider _btcPayWalletProvider;
        private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
        private readonly BitcoinLikePayoutHandler _bitcoinLikePayoutHandler;
        private readonly EventAggregator _eventAggregator;

        public OnChainAutomatedPayoutProcessor(
            ApplicationDbContextFactory applicationDbContextFactory,
            ExplorerClientProvider explorerClientProvider,
            BTCPayWalletProvider btcPayWalletProvider,
            BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
            ILoggerFactory logger,
            BitcoinLikePayoutHandler bitcoinLikePayoutHandler,
            EventAggregator eventAggregator,
            StoreRepository storeRepository,
            PayoutProcessorData payoutProcesserSettings,
            BTCPayNetworkProvider btcPayNetworkProvider) :
            base(logger, storeRepository, payoutProcesserSettings, applicationDbContextFactory,
                btcPayNetworkProvider)
        {
            _explorerClientProvider = explorerClientProvider;
            _btcPayWalletProvider = btcPayWalletProvider;
            _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
            _bitcoinLikePayoutHandler = bitcoinLikePayoutHandler;
            _eventAggregator = eventAggregator;
        }

        protected override async Task Process(ISupportedPaymentMethod paymentMethod, PayoutData[] payouts)
        {
            var storePaymentMethod = paymentMethod as DerivationSchemeSettings;
            if (storePaymentMethod?.IsHotWallet is not true)
            {
                
                return;
            }

            if (!_explorerClientProvider.IsAvailable(PaymentMethodId.CryptoCode))
            {
                return;
            }
            var explorerClient = _explorerClientProvider.GetExplorerClient(PaymentMethodId.CryptoCode);
            var paymentMethodId = PaymentMethodId.Parse(PaymentMethodId.CryptoCode);
            var network = _btcPayNetworkProvider.GetNetwork<BTCPayNetwork>(paymentMethodId.CryptoCode);

            var extKeyStr = await explorerClient.GetMetadataAsync<string>(
                storePaymentMethod.AccountDerivation,
                WellknownMetadataKeys.AccountHDKey);
            if (extKeyStr == null)
            {
                return;
            }

            var wallet = _btcPayWalletProvider.GetWallet(PaymentMethodId.CryptoCode);

            var reccoins = (await wallet.GetUnspentCoins(storePaymentMethod.AccountDerivation)).ToArray();
            var coins = reccoins.Select(coin => coin.Coin).ToArray();

            var accountKey = ExtKey.Parse(extKeyStr, network.NBitcoinNetwork);
            var keys = reccoins.Select(coin => accountKey.Derive(coin.KeyPath).PrivateKey).ToArray();
            Transaction workingTx = null;
            decimal? failedAmount = null;
            var changeAddress = await explorerClient.GetUnusedAsync(
                storePaymentMethod.AccountDerivation, DerivationFeature.Change, 0, true);

            var feeRate = await explorerClient.GetFeeRateAsync(1, new FeeRate(1m));

            var transfersProcessing = new List<PayoutData>();
            foreach (var transferRequest in payouts)
            {
                var blob = transferRequest.GetBlob(_btcPayNetworkJsonSerializerSettings);
                if (failedAmount.HasValue && blob.CryptoAmount >= failedAmount)
                {
                    continue;
                }

                var claimDestination =
                    await _bitcoinLikePayoutHandler.ParseClaimDestination(paymentMethodId, blob.Destination);
                if (!string.IsNullOrEmpty(claimDestination.error))
                {
                    continue;
                }

                var bitcoinClaimDestination = (IBitcoinLikeClaimDestination)claimDestination.destination;
                var txBuilder = network.NBitcoinNetwork.CreateTransactionBuilder()
                    .AddCoins(coins)
                    .AddKeys(keys);
               
                if (workingTx is not null)
                {
                    foreach (var txout in workingTx.Outputs.Where(txout =>
                                 !txout.IsTo(changeAddress.Address)))
                    {
                        txBuilder.Send(txout.ScriptPubKey, txout.Value);
                    }
                }

                txBuilder.Send(bitcoinClaimDestination.Address,
                    new Money(blob.CryptoAmount.Value, MoneyUnit.BTC));

                try
                {
                    txBuilder.SetChange(changeAddress.Address);
                    txBuilder.SendEstimatedFees(feeRate.FeeRate);
                    workingTx = txBuilder.BuildTransaction(true);
                    transfersProcessing.Add(transferRequest);
                }
                catch (NotEnoughFundsException)
                {
                    
                    failedAmount = blob.CryptoAmount;
                    //keep going, we prioritize withdraws by time but if there is some other we can fit, we should
                }
            }

            if (workingTx is not null)
            {
                try
                {
                    await using var context = _applicationDbContextFactory.CreateContext();
                    var txHash = workingTx.GetHash();
                    foreach (PayoutData payoutData in transfersProcessing)
                    {
                        context.Attach(payoutData);
                        payoutData.State = PayoutState.InProgress;
                        _bitcoinLikePayoutHandler.SetProofBlob(payoutData,
                            new PayoutTransactionOnChainBlob()
                            {
                                Accounted = true,
                                TransactionId = txHash,
                                Candidates = new HashSet<uint256>() { txHash }
                            });
                        await context.SaveChangesAsync();
                    }
                    TaskCompletionSource<bool> tcs = new();
                    var cts = new CancellationTokenSource();
                    cts.CancelAfter(TimeSpan.FromSeconds(20));
                    var task = _eventAggregator.WaitNext<NewOnChainTransactionEvent>(
                        e => e.NewTransactionEvent.TransactionData.TransactionHash == txHash,
                        cts.Token);
                    var broadcastResult = await explorerClient.BroadcastAsync(workingTx, cts.Token);
                    if (!broadcastResult.Success)
                    {
                        tcs.SetResult(false);
                    }
                    var walletId = new WalletId(_PayoutProcesserSettings.StoreId, PaymentMethodId.CryptoCode);
                    foreach (PayoutData payoutData in transfersProcessing)
                    {
                        _eventAggregator.Publish(new UpdateTransactionLabel(walletId,
                            txHash,
                            UpdateTransactionLabel.PayoutTemplate(new ()
                            {
                                {payoutData.PullPaymentDataId?? "", new List<string>{payoutData.Id}}
                            }, walletId.ToString())));
                    }
                    await Task.WhenAny(tcs.Task, task);
                }
                catch (OperationCanceledException)
                {
                }
                catch(Exception e)
                {
                    Logs.PayServer.LogError(e, "Could not finalize and broadcast");
                }
            }
        }
    }
}
