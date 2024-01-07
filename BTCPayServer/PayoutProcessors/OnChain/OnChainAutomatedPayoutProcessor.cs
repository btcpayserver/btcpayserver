using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Services;
using BTCPayServer.Services.Stores;
using BTCPayServer.Services.Wallets;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using PayoutData = BTCPayServer.Data.PayoutData;
using PayoutProcessorData = BTCPayServer.Data.PayoutProcessorData;

namespace BTCPayServer.PayoutProcessors.OnChain
{
    public class OnChainAutomatedPayoutProcessor : BaseAutomatedPayoutProcessor<OnChainAutomatedPayoutBlob>
    {
        private readonly ExplorerClientProvider _explorerClientProvider;
        private readonly BTCPayWalletProvider _btcPayWalletProvider;
        private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;
        private readonly BitcoinLikePayoutHandler _bitcoinLikePayoutHandler;

        public OnChainAutomatedPayoutProcessor(
            ApplicationDbContextFactory applicationDbContextFactory,
            ExplorerClientProvider explorerClientProvider,
            BTCPayWalletProvider btcPayWalletProvider,
            BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
            ILoggerFactory logger,
            BitcoinLikePayoutHandler bitcoinLikePayoutHandler,
            EventAggregator eventAggregator,
            WalletRepository walletRepository,
            StoreRepository storeRepository,
            PayoutProcessorData payoutProcesserSettings,
            PullPaymentHostedService pullPaymentHostedService,
            BTCPayNetworkProvider btcPayNetworkProvider,
            IPluginHookService pluginHookService,
            IFeeProviderFactory feeProviderFactory) :
            base(logger, storeRepository, payoutProcesserSettings, applicationDbContextFactory,
                btcPayNetworkProvider, pluginHookService, eventAggregator)
        {
            _explorerClientProvider = explorerClientProvider;
            _btcPayWalletProvider = btcPayWalletProvider;
            _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
            _bitcoinLikePayoutHandler = bitcoinLikePayoutHandler;
            WalletRepository = walletRepository;
            FeeProvider = feeProviderFactory.CreateFeeProvider(_btcPayNetworkProvider.GetNetwork(PaymentMethodId.CryptoCode));
        }

        public WalletRepository WalletRepository { get; }
        public IFeeProvider FeeProvider { get; }

        protected override async Task Process(ISupportedPaymentMethod paymentMethod, List<PayoutData> payouts)
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

            var processorBlob = GetBlob(PayoutProcessorSettings);
            var payoutToBlobs = payouts.ToDictionary(data => data, data => data.GetBlob(_btcPayNetworkJsonSerializerSettings));
            if (payoutToBlobs.Sum(pair => pair.Value.CryptoAmount) < processorBlob.Threshold)
            {
                return;
            }
            
            var feeRate = await FeeProvider.GetFeeRateAsync(Math.Max(processorBlob.FeeTargetBlock, 1));

            var transfersProcessing = new List<KeyValuePair<PayoutData, PayoutBlob>>();
            foreach (var transferRequest in payoutToBlobs)
            {
                var blob = transferRequest.Value;
                if (failedAmount.HasValue && blob.CryptoAmount >= failedAmount)
                {
                    continue;
                }

                var claimDestination =
                    await _bitcoinLikePayoutHandler.ParseClaimDestination(paymentMethodId, blob.Destination, CancellationToken);
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
                    txBuilder.SendEstimatedFees(feeRate);
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
                    var txHash = workingTx.GetHash();
                    foreach (var payoutData in transfersProcessing)
                    {
                        payoutData.Key.State = PayoutState.InProgress;
                        _bitcoinLikePayoutHandler.SetProofBlob(payoutData.Key,
                            new PayoutTransactionOnChainBlob()
                            {
                                Accounted = true,
                                TransactionId = txHash,
                                Candidates = new HashSet<uint256>() { txHash }
                            });
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
                    var walletId = new WalletId(PayoutProcessorSettings.StoreId, PaymentMethodId.CryptoCode);
                    foreach (var payoutData in transfersProcessing)
                    {
                        await WalletRepository.AddWalletTransactionAttachment(walletId,
                            txHash,
                            Attachment.Payout(payoutData.Key.PullPaymentDataId, payoutData.Key.Id));
                    }
                    await Task.WhenAny(tcs.Task, task);
                }
                catch (OperationCanceledException)
                {
                }
                catch (Exception e)
                {
                    Logs.PayServer.LogError(e, "Could not finalize and broadcast");
                }
            }
        }
    }
}
