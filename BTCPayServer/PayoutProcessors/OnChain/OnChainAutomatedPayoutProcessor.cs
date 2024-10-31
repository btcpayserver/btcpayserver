using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payouts;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
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

        public BTCPayNetwork Network { get; }

		private readonly IFeeProviderFactory _feeProviderFactory;

		public OnChainAutomatedPayoutProcessor(
            PayoutMethodId payoutMethodId,
            ApplicationDbContextFactory applicationDbContextFactory,
			ExplorerClientProvider explorerClientProvider,
			BTCPayWalletProvider btcPayWalletProvider,
			BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings,
			ILoggerFactory logger,
			EventAggregator eventAggregator,
			WalletRepository walletRepository,
			StoreRepository storeRepository,
			PayoutProcessorData payoutProcesserSettings,
			PullPaymentHostedService pullPaymentHostedService,
            PayoutMethodHandlerDictionary payoutHandlers,
            PaymentMethodHandlerDictionary handlers,
			IPluginHookService pluginHookService,
			IFeeProviderFactory feeProviderFactory) :
			base(
                PaymentTypes.CHAIN.GetPaymentMethodId(GetPayoutHandler(payoutHandlers, payoutMethodId).Network.CryptoCode),
                logger, storeRepository, payoutProcesserSettings, applicationDbContextFactory,
				handlers, pluginHookService, eventAggregator)
        {
            _explorerClientProvider = explorerClientProvider;
            _btcPayWalletProvider = btcPayWalletProvider;
            _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
            _bitcoinLikePayoutHandler = GetPayoutHandler(payoutHandlers, payoutMethodId);
            Network = _bitcoinLikePayoutHandler.Network;
            WalletRepository = walletRepository;
            _feeProviderFactory = feeProviderFactory;
        }

        private static BitcoinLikePayoutHandler GetPayoutHandler(PayoutMethodHandlerDictionary payoutHandlers, PayoutMethodId payoutMethodId)
        {
            return (BitcoinLikePayoutHandler)payoutHandlers[payoutMethodId];
        }

        public WalletRepository WalletRepository { get; }

		protected override async Task Process(object paymentMethodConfig, List<PayoutData> payouts)
		{
			if (paymentMethodConfig is not DerivationSchemeSettings { IsHotWallet: true } config)
            {
                DisableProcessor(payouts);
                return;
            }
            if (!_explorerClientProvider.IsAvailable(Network.CryptoCode))
            {
                return;
            }
            
            var explorerClient = _explorerClientProvider.GetExplorerClient(Network.CryptoCode);

            var extKeyStr = await explorerClient.GetMetadataAsync<string>(
                config.AccountDerivation,
                WellknownMetadataKeys.AccountHDKey);
            if (extKeyStr == null)
            {
                DisableProcessor(payouts);
                return;
            }

            var wallet = _btcPayWalletProvider.GetWallet(Network.CryptoCode);

            var reccoins = (await wallet.GetUnspentCoins(config.AccountDerivation)).ToArray();
            var coins = reccoins.Select(coin => coin.Coin).ToArray();

            var accountKey = ExtKey.Parse(extKeyStr, Network.NBitcoinNetwork);
            var keys = reccoins.Select(coin => accountKey.Derive(coin.KeyPath).PrivateKey).ToArray();
            Transaction workingTx = null;
            decimal? failedAmount = null;
            var changeAddress = await explorerClient.GetUnusedAsync(
                config.AccountDerivation, DerivationFeature.Change, 0, true);

            var processorBlob = GetBlob(PayoutProcessorSettings);
            if (payouts.Sum(p => p.Amount) < processorBlob.Threshold)
                return;
            
            var feeRate = await this._feeProviderFactory.CreateFeeProvider(Network).GetFeeRateAsync(Math.Max(processorBlob.FeeTargetBlock, 1));

            var transfersProcessing = new List<PayoutData>();
            foreach (var payout in payouts)
            {
                var blob = payout.GetBlob(_btcPayNetworkJsonSerializerSettings);
                if (failedAmount.HasValue && payout.Amount >= failedAmount)
                {
                    continue;
                }

                var claimDestination =
                    await _bitcoinLikePayoutHandler.ParseClaimDestination(blob.Destination, CancellationToken);
                if (!string.IsNullOrEmpty(claimDestination.error))
                {
                    DisableProcessor([payout]);
                    continue;
                }

                var bitcoinClaimDestination = (IBitcoinLikeClaimDestination)claimDestination.destination;
                var txBuilder = Network.NBitcoinNetwork.CreateTransactionBuilder()
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
                    new Money(payout.Amount.Value, MoneyUnit.BTC));

                try
                {
                    txBuilder.SetChange(changeAddress.Address);
                    txBuilder.SendEstimatedFees(feeRate);
                    workingTx = txBuilder.BuildTransaction(true);
                    transfersProcessing.Add(payout);
                }
                catch (NotEnoughFundsException)
                {
					failedAmount = payout.Amount;
					if (blob.IncrementErrorCount() >= 10)
						blob.DisableProcessor(OnChainAutomatedPayoutSenderFactory.ProcessorName);
					payout.SetBlob(blob, _btcPayNetworkJsonSerializerSettings);
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
                        payoutData.State = PayoutState.InProgress;
                        _bitcoinLikePayoutHandler.SetProofBlob(payoutData,
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
                    var walletId = new WalletId(PayoutProcessorSettings.StoreId, Network.CryptoCode);
                    foreach (var payoutData in transfersProcessing)
                    {
                        await WalletRepository.AddWalletTransactionAttachment(walletId,
                            txHash,
                            Attachment.Payout(payoutData.PullPaymentDataId, payoutData.Id));
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

        private void DisableProcessor(List<PayoutData> payouts)
        {
            foreach (var payout in payouts)
            {
                var b = payout.GetBlob(_btcPayNetworkJsonSerializerSettings);
                b.DisableProcessor(OnChainAutomatedPayoutSenderFactory.ProcessorName);
                payout.SetBlob(b, _btcPayNetworkJsonSerializerSettings);
            }
        }
    }
}
