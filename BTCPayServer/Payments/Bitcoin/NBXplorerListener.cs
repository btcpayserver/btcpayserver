using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments.PayJoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;

namespace BTCPayServer.Payments.Bitcoin
{
    /// <summary>
    /// This class listener NBXplorer instances to detect incoming on-chain, bitcoin like payment
    /// </summary>
    public class NBXplorerListener : IHostedService
    {
        readonly EventAggregator _Aggregator;
        private readonly PayJoinRepository _payJoinRepository;
        readonly ExplorerClientProvider _ExplorerClients;
        readonly IHostApplicationLifetime _Lifetime;
        readonly InvoiceRepository _InvoiceRepository;
        private TaskCompletionSource<bool> _RunningTask;
        private CancellationTokenSource _Cts;
        readonly BTCPayWalletProvider _Wallets;
        public NBXplorerListener(ExplorerClientProvider explorerClients,
                                BTCPayWalletProvider wallets,
                                InvoiceRepository invoiceRepository,
                                EventAggregator aggregator,
                                PayJoinRepository payjoinRepository,
                                IHostApplicationLifetime lifetime)
        {
            PollInterval = TimeSpan.FromMinutes(1.0);
            _Wallets = wallets;
            _InvoiceRepository = invoiceRepository;
            _ExplorerClients = explorerClients;
            _Aggregator = aggregator;
            _payJoinRepository = payjoinRepository;
            _Lifetime = lifetime;
        }

        readonly CompositeDisposable leases = new CompositeDisposable();
        readonly ConcurrentDictionary<string, WebsocketNotificationSession> _SessionsByCryptoCode = new ConcurrentDictionary<string, WebsocketNotificationSession>();
        private Timer _ListenPoller;

        TimeSpan _PollInterval;
        public TimeSpan PollInterval
        {
            get
            {
                return _PollInterval;
            }
            set
            {
                _PollInterval = value;
                if (_ListenPoller != null)
                {
                    _ListenPoller.Change(0, (int)value.TotalMilliseconds);
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _RunningTask = new TaskCompletionSource<bool>();
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            leases.Add(_Aggregator.Subscribe<Events.NBXplorerStateChangedEvent>(async nbxplorerEvent =>
            {
                if (nbxplorerEvent.NewState == NBXplorerState.Ready)
                {
                    var wallet = _Wallets.GetWallet(nbxplorerEvent.Network);
                    if (_Wallets.IsAvailable(wallet.Network))
                    {
                        await Listen(wallet);
                    }
                }
            }));

            _ListenPoller = new Timer(async s =>
            {
                foreach (var wallet in _Wallets.GetWallets())
                {
                    if (_Wallets.IsAvailable(wallet.Network))
                    {
                        await Listen(wallet);
                    }
                }
            }, null, 0, (int)PollInterval.TotalMilliseconds);
            leases.Add(_ListenPoller);
            return Task.CompletedTask;
        }

        private async Task Listen(BTCPayWallet wallet)
        {
            var network = wallet.Network;
            bool cleanup = false;
            try
            {
                if (_SessionsByCryptoCode.ContainsKey(network.CryptoCode))
                    return;
                var client = _ExplorerClients.GetExplorerClient(network);
                if (client == null)
                    return;
                if (_Cts.IsCancellationRequested)
                    return;
                var session = await client.CreateWebsocketNotificationSessionAsync(_Cts.Token).ConfigureAwait(false);
                if (!_SessionsByCryptoCode.TryAdd(network.CryptoCode, session))
                {
                    await session.DisposeAsync();
                    return;
                }
                cleanup = true;

                using (session)
                {
                    await session.ListenNewBlockAsync(_Cts.Token).ConfigureAwait(false);
                    await session.ListenAllTrackedSourceAsync(cancellation: _Cts.Token).ConfigureAwait(false);

                    Logs.PayServer.LogInformation($"{network.CryptoCode}: Checking if any pending invoice got paid while offline...");
                    int paymentCount = await FindPaymentViaPolling(wallet, network);
                    Logs.PayServer.LogInformation($"{network.CryptoCode}: {paymentCount} payments happened while offline");

                    Logs.PayServer.LogInformation($"Connected to WebSocket of NBXplorer ({network.CryptoCode})");
                    while (!_Cts.IsCancellationRequested)
                    {
                        var newEvent = await session.NextEventAsync(_Cts.Token).ConfigureAwait(false);
                        switch (newEvent)
                        {
                            case NBXplorer.Models.NewBlockEvent evt:
                                await UpdatePaymentStates(wallet, await _InvoiceRepository.GetPendingInvoices());
                                _Aggregator.Publish(new Events.NewBlockEvent() { CryptoCode = evt.CryptoCode });
                                break;
                            case NBXplorer.Models.NewTransactionEvent evt:
                                if (evt.DerivationStrategy != null)
                                {
                                    wallet.InvalidateCache(evt.DerivationStrategy);

                                    foreach (var output in network.GetValidOutputs(evt))
                                    {
                                        var key = output.Item1.ScriptPubKey.Hash + "#" +
                                                  network.CryptoCode.ToUpperInvariant();
                                        var invoice = (await _InvoiceRepository.GetInvoicesFromAddresses(new[] {key}))
                                            .FirstOrDefault();
                                        if (invoice != null)
                                        {
                                            var address = network.NBXplorerNetwork.CreateAddress(evt.DerivationStrategy,
                                                output.Item1.KeyPath, output.Item1.ScriptPubKey);

                                            var paymentData = new BitcoinLikePaymentData(address,
                                                output.matchedOutput.Value, output.outPoint,
                                                evt.TransactionData.Transaction.RBF, output.matchedOutput.KeyPath);

                                            var alreadyExist = invoice
                                                .GetAllBitcoinPaymentData().Any(c => c.GetPaymentId() == paymentData.GetPaymentId());
                                            if (!alreadyExist)
                                            {
                                                var payment = await _InvoiceRepository.AddPayment(invoice.Id,
                                                    DateTimeOffset.UtcNow, paymentData, network);
                                                if (payment != null)
                                                    await ReceivedPayment(wallet, invoice, payment,
                                                        evt.DerivationStrategy);
                                            }
                                            else
                                            {
                                                await UpdatePaymentStates(wallet, invoice.Id);
                                            }
                                        }

                                    }
                                }

                                _Aggregator.Publish(new NewOnChainTransactionEvent()
                                {
                                    CryptoCode = wallet.Network.CryptoCode,
                                    NewTransactionEvent = evt
                                });

                                break;
                            default:
                                Logs.PayServer.LogWarning("Received unknown message from NBXplorer");
                                break;
                        }
                    }
                }
            }
            catch when (_Cts.IsCancellationRequested) { }
            catch (Exception ex)
            {
                Logs.PayServer.LogError(ex, $"Error while connecting to WebSocket of NBXplorer ({network.CryptoCode})");
            }
            finally
            {
                if (cleanup)
                {
                    Logs.PayServer.LogInformation($"Disconnected from WebSocket of NBXplorer ({network.CryptoCode})");
                    _SessionsByCryptoCode.TryRemove(network.CryptoCode, out WebsocketNotificationSession unused);
                    if (_SessionsByCryptoCode.Count == 0 && _Cts.IsCancellationRequested)
                    {
                        _RunningTask.TrySetResult(true);
                    }
                }
            }
        }

        async Task UpdatePaymentStates(BTCPayWallet wallet, string[] invoiceIds)
        {
            var invoices = await _InvoiceRepository.GetInvoices(invoiceIds);
            await Task.WhenAll(invoices.Select(i => UpdatePaymentStates(wallet, i)).ToArray());
        }
        async Task<InvoiceEntity> UpdatePaymentStates(BTCPayWallet wallet, string invoiceId)
        {
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId, false);
            if (invoice == null)
                return null;
            return await UpdatePaymentStates(wallet, invoice);
        }
        async Task<InvoiceEntity> UpdatePaymentStates(BTCPayWallet wallet, InvoiceEntity invoice)
        {

            List<PaymentEntity> updatedPaymentEntities = new List<PaymentEntity>();
            var transactions = await wallet.GetTransactions(invoice.GetAllBitcoinPaymentData(false)
                    .Select(p => p.Outpoint.Hash)
                    .ToArray(), true);
            bool? originalPJBroadcasted = null;
            bool? originalPJBroadcastable = null;
            bool cjPJBroadcasted = false;
            PayjoinInformation payjoinInformation = null;
            var paymentEntitiesByPrevOut = new Dictionary<OutPoint, PaymentEntity>();
            foreach (var payment in invoice.GetPayments(wallet.Network, false))
            {
                if (payment.GetPaymentMethodId()?.PaymentType != PaymentTypes.BTCLike)
                    continue;
                var paymentData = (BitcoinLikePaymentData)payment.GetCryptoPaymentData();
                if (!transactions.TryGetValue(paymentData.Outpoint.Hash, out TransactionResult tx))
                    continue;

                bool accounted = true;

                if (tx.Confirmations == 0 || tx.Confirmations == -1)
                {
                    // Let's check if it was orphaned by broadcasting it again
                    var explorerClient = _ExplorerClients.GetExplorerClient(wallet.Network);
                    try
                    {
                        var result = await explorerClient.BroadcastAsync(tx.Transaction, testMempoolAccept: tx.Confirmations == -1, _Cts.Token);
                        accounted = result.Success ||
                                    result.RPCCode == RPCErrorCode.RPC_TRANSACTION_ALREADY_IN_CHAIN ||
                                    !(
                                    // Happen if a blocks mined a replacement
                                    // Or if the tx is a double spend of something already in the mempool without rbf
                                    result.RPCCode == RPCErrorCode.RPC_TRANSACTION_ERROR ||
                                    // Happen if RBF is on and fee insufficient
                                    result.RPCCode == RPCErrorCode.RPC_TRANSACTION_REJECTED);
                        if (!accounted && payment.Accounted && tx.Confirmations != -1)
                        {
                            Logs.PayServer.LogInformation($"{wallet.Network.CryptoCode}: The transaction {tx.TransactionHash} has been replaced.");
                        }
                        if (paymentData.PayjoinInformation is PayjoinInformation pj)
                        {
                            payjoinInformation = pj;
                            originalPJBroadcasted = accounted && tx.Confirmations >= 0;
                            originalPJBroadcastable = accounted;
                        }
                    }
                    // RPC might be unavailable, we can't check double spend so let's assume there is none
                    catch
                    {

                    }
                }

                bool updated = false;
                if (accounted != payment.Accounted)
                {
                    // If a payment is replacing another, use the same network fee as the replaced one.
                    if (accounted)
                    {
                        foreach (var prevout in tx.Transaction.Inputs.Select(o => o.PrevOut))
                        {
                            if (paymentEntitiesByPrevOut.TryGetValue(prevout, out var replaced) && !replaced.Accounted)
                            {
                                payment.NetworkFee = replaced.NetworkFee;
                                if (payjoinInformation is PayjoinInformation pj &&
                                    pj.CoinjoinTransactionHash == tx.TransactionHash)
                                {
                                    // This payment is a coinjoin, so the value of
                                    // the payment output is different from the real value of the payment 
                                    paymentData.Value = pj.CoinjoinValue;
                                    payment.SetCryptoPaymentData(paymentData);
                                }
                            }
                        }
                    }
                    payment.Accounted = accounted;
                    updated = true;
                }

                foreach (var prevout in tx.Transaction.Inputs.Select(o => o.PrevOut))
                {
                    paymentEntitiesByPrevOut.TryAdd(prevout, payment);
                }

                if (paymentData.ConfirmationCount != tx.Confirmations)
                {
                    if (wallet.Network.MaxTrackedConfirmation >= paymentData.ConfirmationCount)
                    {
                        paymentData.ConfirmationCount = tx.Confirmations;
                        payment.SetCryptoPaymentData(paymentData);
                        updated = true;
                    }
                }

                // if needed add invoice back to pending to track number of confirmations
                if (paymentData.ConfirmationCount < wallet.Network.MaxTrackedConfirmation)
                    await _InvoiceRepository.AddPendingInvoiceIfNotPresent(invoice.Id);

                if (updated)
                    updatedPaymentEntities.Add(payment);
            }

            // If the origin tx of a payjoin has been broadcasted, then we know we can
            // reuse our outpoint for another PJ
            if (originalPJBroadcasted is true ||
                // If the original tx is not broadcastable anymore and nor does the coinjoin
                // reuse our outpoint for another PJ
                (originalPJBroadcastable is false && !cjPJBroadcasted))
            {
                await _payJoinRepository.TryUnlock(payjoinInformation.ContributedOutPoints);
            }

            await _InvoiceRepository.UpdatePayments(updatedPaymentEntities);
            if (updatedPaymentEntities.Count != 0)
                _Aggregator.Publish(new Events.InvoiceNeedUpdateEvent(invoice.Id));
            return invoice;
        }

        private async Task<int> FindPaymentViaPolling(BTCPayWallet wallet, BTCPayNetwork network)
        {
            int totalPayment = 0;
            var invoices = await _InvoiceRepository.GetPendingInvoices();
            var coinsPerDerivationStrategy =
                new Dictionary<DerivationStrategyBase, ReceivedCoin[]>();
            foreach (var invoiceId in invoices)
            {
                var invoice = await _InvoiceRepository.GetInvoice(invoiceId, true);
                if (invoice == null)
                    continue;
                var alreadyAccounted = invoice.GetAllBitcoinPaymentData(false).Select(p => p.Outpoint).ToHashSet();
                var strategy = GetDerivationStrategy(invoice, network);
                if (strategy == null)
                    continue;
                var cryptoId = new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike);

                if (!invoice.Support(cryptoId))
                    continue;

                if (!coinsPerDerivationStrategy.TryGetValue(strategy, out var coins))
                {
                    coins = await wallet.GetUnspentCoins(strategy);
                    coinsPerDerivationStrategy.Add(strategy, coins);
                }
                coins = coins.Where(c => invoice.AvailableAddressHashes.Contains(c.ScriptPubKey.Hash.ToString() + cryptoId))
                             .ToArray();
                foreach (var coin in coins.Where(c => !alreadyAccounted.Contains(c.OutPoint)))
                {
                    var transaction = await wallet.GetTransactionAsync(coin.OutPoint.Hash);

                    var address = network.NBXplorerNetwork.CreateAddress(strategy, coin.KeyPath, coin.ScriptPubKey);

                    var paymentData = new BitcoinLikePaymentData(address, coin.Value, coin.OutPoint,
                        transaction?.Transaction is null ? true : transaction.Transaction.RBF, coin.KeyPath);

                    var payment = await _InvoiceRepository.AddPayment(invoice.Id, coin.Timestamp, paymentData, network).ConfigureAwait(false);
                    alreadyAccounted.Add(coin.OutPoint);
                    if (payment != null)
                    {
                        invoice = await ReceivedPayment(wallet, invoice, payment, strategy);
                        if (invoice == null)
                            continue;
                        totalPayment++;
                    }
                }
            }
            return totalPayment;
        }

        private DerivationStrategyBase GetDerivationStrategy(InvoiceEntity invoice, BTCPayNetworkBase network)
        {
            return invoice.GetSupportedPaymentMethod<DerivationSchemeSettings>(new PaymentMethodId(network.CryptoCode, PaymentTypes.BTCLike))
                          .Select(d => d.AccountDerivation)
                          .FirstOrDefault();
        }

        private async Task<InvoiceEntity> ReceivedPayment(BTCPayWallet wallet, InvoiceEntity invoice, PaymentEntity payment, DerivationStrategyBase strategy)
        {
            var paymentData = (BitcoinLikePaymentData)payment.GetCryptoPaymentData();
            invoice = (await UpdatePaymentStates(wallet, invoice.Id));
            if (invoice == null)
                return null;
            var paymentMethod = invoice.GetPaymentMethod(wallet.Network, PaymentTypes.BTCLike);
            if (paymentMethod != null &&
                paymentMethod.GetPaymentMethodDetails() is BitcoinLikeOnChainPaymentMethod btc &&
                btc.Activated && 
                btc.GetDepositAddress(wallet.Network.NBitcoinNetwork).ScriptPubKey == paymentData.ScriptPubKey &&
                paymentMethod.Calculate().Due > Money.Zero)
            {
                var address = await wallet.ReserveAddressAsync(strategy);
                btc.DepositAddress = address.Address.ToString();
                btc.KeyPath = address.KeyPath;
                await _InvoiceRepository.NewPaymentDetails(invoice.Id, btc, wallet.Network);
                _Aggregator.Publish(new InvoiceNewPaymentDetailsEvent(invoice.Id, btc, paymentMethod.GetId()));
                paymentMethod.SetPaymentMethodDetails(btc);
                invoice.SetPaymentMethod(paymentMethod);
            }
            wallet.InvalidateCache(strategy);
            _Aggregator.Publish(new InvoiceEvent(invoice, InvoiceEvent.ReceivedPayment) { Payment = payment });
            return invoice;
        }
        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_Cts != null)
            {
                leases.Dispose();
                _Cts.Cancel();
                await Task.WhenAny(_RunningTask.Task, Task.Delay(-1, cancellationToken));
                Logs.PayServer.LogInformation($"{this.GetType().Name} successfully exited...");
            }
        }
    }
}
