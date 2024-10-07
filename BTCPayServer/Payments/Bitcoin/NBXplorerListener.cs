using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments.PayJoin;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Wallets;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBitcoin.RPC;
using NBXplorer;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments.Bitcoin
{
    /// <summary>
    /// This class listener NBXplorer instances to detect incoming on-chain, bitcoin like payment
    /// </summary>
    public class NBXplorerListener : IHostedService
    {
        readonly EventAggregator _Aggregator;
        private readonly UTXOLocker _utxoLocker;
        readonly ExplorerClientProvider _ExplorerClients;
        private readonly PaymentService _paymentService;
        private readonly PaymentMethodHandlerDictionary _handlers;
        readonly InvoiceRepository _InvoiceRepository;
        private TaskCompletionSource<bool> _RunningTask;
        private CancellationTokenSource _Cts;
        readonly BTCPayWalletProvider _Wallets;
        public NBXplorerListener(ExplorerClientProvider explorerClients,
                                BTCPayWalletProvider wallets,
                                InvoiceRepository invoiceRepository,
                                EventAggregator aggregator,
                                UTXOLocker payjoinRepository,
                                PaymentService paymentService,
                                PaymentMethodHandlerDictionary handlers,
                                Logs logs)
        {
            this.Logs = logs;
            PollInterval = TimeSpan.FromMinutes(1.0);
            _Wallets = wallets;
            _InvoiceRepository = invoiceRepository;
            _ExplorerClients = explorerClients;
            _Aggregator = aggregator;
            _utxoLocker = payjoinRepository;
            _paymentService = paymentService;
            _handlers = handlers;
        }

        readonly CompositeDisposable leases = new CompositeDisposable();
        readonly ConcurrentDictionary<string, WebsocketNotificationSession> _SessionsByCryptoCode = new ConcurrentDictionary<string, WebsocketNotificationSession>();
        private Timer _ListenPoller;

        TimeSpan _PollInterval;

        public Logs Logs { get; }

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
            leases.Add(_Aggregator.Subscribe<Events.NBXplorerStateChangedEvent>(nbxplorerEvent =>
            {
                if (nbxplorerEvent.NewState == NBXplorerState.Ready)
                {
                    var wallet = _Wallets.GetWallet(nbxplorerEvent.Network);
                    if (_Wallets.IsAvailable(wallet.Network))
                    {
                        _ = Listen(wallet);
                    }
                }
            }));

            _ListenPoller = new Timer(s =>
            {
                foreach (var wallet in _Wallets.GetWallets())
                {
                    if (_Wallets.IsAvailable(wallet.Network))
                    {
                        _ = Listen(wallet);
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
                    var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);
                    while (!_Cts.IsCancellationRequested)
                    {
                        var newEvent = await session.NextEventAsync(_Cts.Token).ConfigureAwait(false);
                        switch (newEvent)
                        {
                            case NBXplorer.Models.NewBlockEvent evt:
                                await UpdatePaymentStates(wallet);
                                _Aggregator.Publish(new Events.NewBlockEvent() { PaymentMethodId = pmi });
                                break;
                            case NBXplorer.Models.NewTransactionEvent evt:
                                if (evt.DerivationStrategy != null)
                                {
                                    wallet.InvalidateCache(evt.DerivationStrategy);
                                    var validOutputs = network.GetValidOutputs(evt).ToList();
                                    if (!validOutputs.Any())
                                        break;
                                    foreach (var output in validOutputs)
                                    {
                                        var key = network.GetTrackedDestination(output.Item1.ScriptPubKey);
                                        var invoice = await _InvoiceRepository.GetInvoiceFromAddress(pmi, key);
                                        if (invoice != null)
                                        {
                                            var address = output.matchedOutput.Address ?? network.NBXplorerNetwork.CreateAddress(evt.DerivationStrategy,
                                                output.Item1.KeyPath, output.Item1.ScriptPubKey);
                                            var handler = _handlers[pmi];
                                            var details = new BitcoinLikePaymentData(output.outPoint, evt.TransactionData.Transaction.RBF, output.matchedOutput.KeyPath);

                                            var paymentData = new Data.PaymentData()
                                            {
                                                Id = output.outPoint.ToString(),
                                                Created = DateTimeOffset.UtcNow,
                                                Status = IsSettled(invoice, details) ? PaymentStatus.Settled : PaymentStatus.Processing,
                                                Amount = ((Money)output.matchedOutput.Value).ToDecimal(MoneyUnit.BTC),
                                                Currency = network.CryptoCode
                                            }.Set(invoice, handler, details);

                                            var alreadyExist = invoice
                                                .GetPayments(false).Any(c => c.Id == paymentData.Id && c.PaymentMethodId == pmi);
                                            if (!alreadyExist)
                                            {
                                                
                                                var prompt = invoice.GetPaymentPrompt(pmi);
                                                var payment = await _paymentService.AddPayment(paymentData, [output.outPoint.Hash.ToString()]);
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
                                    PaymentMethodId = pmi,
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
                    if (_SessionsByCryptoCode.IsEmpty && _Cts.IsCancellationRequested)
                    {
                        _RunningTask.TrySetResult(true);
                    }
                }
            }
        }

        async Task UpdatePaymentStates(BTCPayWallet wallet)
        {
            var invoices = await _InvoiceRepository.GetMonitoredInvoices(PaymentTypes.CHAIN.GetPaymentMethodId(wallet.Network.CryptoCode));
            await Task.WhenAll(invoices.Select(i => UpdatePaymentStates(wallet, i)).ToArray());
        }
        async Task<InvoiceEntity> UpdatePaymentStates(BTCPayWallet wallet, string invoiceId, bool fireEvents = true)
        {
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId, false);
            if (invoice == null)
                return null;
            return await UpdatePaymentStates(wallet, invoice, fireEvents);
        }
        async Task<InvoiceEntity> UpdatePaymentStates(BTCPayWallet wallet, InvoiceEntity invoice, bool fireEvents = true)
        {
            var pmi = PaymentTypes.CHAIN.GetPaymentMethodId(wallet.Network.CryptoCode);
            var handler = (BitcoinLikePaymentHandler)_handlers[pmi];
            List<PaymentEntity> updatedPaymentEntities = new List<PaymentEntity>();
            var transactions = await wallet.GetTransactions(invoice.GetPayments(false)
                    .Where(p => p.PaymentMethodId == pmi)
                    .Select(p => handler.ParsePaymentDetails(p.Details).Outpoint.Hash)
                    .ToArray(), true);
            bool? originalPJBroadcasted = null;
            bool? originalPJBroadcastable = null;
            bool cjPJBroadcasted = false;
            PayjoinInformation payjoinInformation = null;
            var paymentEntitiesByPrevOut = new Dictionary<OutPoint, PaymentEntity>();
            foreach (var payment in invoice.GetPayments(false).Where(p => p.PaymentMethodId == pmi))
            {
                var paymentData = handler.ParsePaymentDetails(payment.Details);
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
                if (paymentData.ConfirmationCount != tx.Confirmations)
                {
                    var oldStatus = payment.Status;
                    var oldConfCount = paymentData.ConfirmationCount;
                    paymentData.ConfirmationCount = Math.Min(tx.Confirmations, wallet.Network.MaxTrackedConfirmation);
                    if (oldConfCount != paymentData.ConfirmationCount)
                    {
                        payment.SetDetails(handler, paymentData);
                        updated = true;
                    }
                }

                var prevStatus = payment.Status;
                // If a payment is replacing another, use the same network fee as the replaced one.
                if (accounted)
                {
                    foreach (var prevout in tx.Transaction.Inputs.Select(o => o.PrevOut))
                    {
                        if (paymentEntitiesByPrevOut.TryGetValue(prevout, out var replaced) && !replaced.Accounted)
                        {
                            payment.PaymentMethodFee = replaced.PaymentMethodFee;
                            if (payjoinInformation is PayjoinInformation pj &&
                                pj.CoinjoinTransactionHash == tx.TransactionHash)
                            {
                                // This payment is a coinjoin, so the value of
                                // the payment output is different from the real value of the payment 
                                payment.Value = pj.CoinjoinValue.ToDecimal(MoneyUnit.BTC);
                                payment.SetDetails(handler, paymentData);
                            }
                            updated = true;
                        }
                    }
                    payment.Status = IsSettled(invoice, paymentData) ? PaymentStatus.Settled : PaymentStatus.Processing;
                }
                else
                {
                    payment.Status = PaymentStatus.Unaccounted;
                }
                updated |= prevStatus != payment.Status;

                foreach (var prevout in tx.Transaction.Inputs.Select(o => o.PrevOut))
                {
                    paymentEntitiesByPrevOut.TryAdd(prevout, payment);
                }

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
                await _utxoLocker.TryUnlock(payjoinInformation.ContributedOutPoints);
            }

            await _paymentService.UpdatePayments(updatedPaymentEntities);
            if (fireEvents && updatedPaymentEntities.Count != 0)
                _Aggregator.Publish(new Events.InvoiceNeedUpdateEvent(invoice.Id));
            return invoice;
        }

        public static int ConfirmationRequired(InvoiceEntity invoice, BitcoinLikePaymentData paymentData)
        => (invoice, paymentData) switch
        {
            ({ SpeedPolicy: SpeedPolicy.HighSpeed }, { RBF: true }) => 1,
            ({ SpeedPolicy: SpeedPolicy.HighSpeed }, _) => 0,
            ({ SpeedPolicy: SpeedPolicy.MediumSpeed }, _) => 1,
            ({ SpeedPolicy: SpeedPolicy.LowMediumSpeed }, _) => 2,
            ({ SpeedPolicy: SpeedPolicy.LowSpeed }, _) => 6,
            _ => 6,
        };

        static bool IsSettled(InvoiceEntity invoice, BitcoinLikePaymentData paymentData)
            => ConfirmationRequired(invoice, paymentData) <= paymentData.ConfirmationCount;

        private async Task<int> FindPaymentViaPolling(BTCPayWallet wallet, BTCPayNetwork network)
        {
            var handler = _handlers.GetBitcoinHandler(wallet.Network);
            int totalPayment = 0;
            var invoices = await _InvoiceRepository.GetMonitoredInvoices(PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode));
            var coinsPerDerivationStrategy =
                new Dictionary<DerivationStrategyBase, ReceivedCoin[]>();
            foreach (var i in invoices)
            {
                var invoice = i;
                var alreadyAccounted = invoice.GetAllBitcoinPaymentData(handler, false).Select(p => p.Outpoint).ToHashSet();
                var strategy = _handlers.GetDerivationStrategy(invoice, network);
                if (strategy == null)
                    continue;
                var cryptoId = PaymentTypes.CHAIN.GetPaymentMethodId(network.CryptoCode);

                if (!invoice.Support(cryptoId))
                    continue;

                if (!coinsPerDerivationStrategy.TryGetValue(strategy, out var coins))
                {
                    coins = await wallet.GetUnspentCoins(strategy);
                    coinsPerDerivationStrategy.Add(strategy, coins);
                }
                coins = coins.Where(c => invoice.Addresses.Contains((cryptoId, network.GetTrackedDestination(c.ScriptPubKey)))).ToArray();
                foreach (var coin in coins.Where(c => !alreadyAccounted.Contains(c.OutPoint)))
                {
                    var transaction = await wallet.GetTransactionAsync(coin.OutPoint.Hash);

                    var address = network.NBXplorerNetwork.CreateAddress(strategy, coin.KeyPath, coin.ScriptPubKey);

                    var paymentData = new Data.PaymentData()
                    {
                        Id = coin.OutPoint.ToString(),
                        Created = DateTimeOffset.UtcNow,
                        Status = PaymentStatus.Processing,
                        Amount = ((Money)coin.Value).ToDecimal(MoneyUnit.BTC),
                        Currency = network.CryptoCode
                    }.Set(invoice, handler, new BitcoinLikePaymentData(coin.OutPoint, transaction?.Transaction is null ? true : transaction.Transaction.RBF, coin.KeyPath));

                    var payment = await _paymentService.AddPayment(paymentData, [coin.OutPoint.Hash.ToString()]).ConfigureAwait(false);
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

        private async Task<InvoiceEntity> ReceivedPayment(BTCPayWallet wallet, InvoiceEntity invoice, PaymentEntity payment, DerivationStrategyBase strategy)
        {
            // We want the invoice watcher to look at our invoice after we bumped the payment method fee, so fireEvent=false.
            invoice = (await UpdatePaymentStates(wallet, invoice.Id, fireEvents: false));
            if (invoice == null)
                return null;
            var prompt = invoice.GetPaymentPrompt(payment.PaymentMethodId);
            if (!_handlers.TryGetValue(prompt.PaymentMethodId, out var handler))
                return null;
            var bitcoinPaymentMethod = (Payments.Bitcoin.BitcoinPaymentPromptDetails)_handlers.ParsePaymentPromptDetails(prompt);
            if (bitcoinPaymentMethod.FeeMode == NetworkFeeMode.MultiplePaymentsOnly &&
                prompt.PaymentMethodFee == 0.0m)
            {
                prompt.PaymentMethodFee = bitcoinPaymentMethod.PaymentMethodFeeRate.GetFee(100).ToDecimal(MoneyUnit.BTC); // assume price for 100 bytes
                await this._InvoiceRepository.UpdatePrompt(invoice.Id, prompt);
                invoice = await _InvoiceRepository.GetInvoice(invoice.Id);
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
