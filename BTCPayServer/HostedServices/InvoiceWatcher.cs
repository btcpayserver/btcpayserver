using NBXplorer;
using Microsoft.Extensions.Logging;
using NBXplorer.DerivationStrategy;
using NBXplorer.Models;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using BTCPayServer.Logging;
using System.Threading;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using Hangfire;
using BTCPayServer.Services.Wallets;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using Microsoft.AspNetCore.Hosting;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.HostedServices
{
    public class InvoiceWatcher : IHostedService
    {
        class UpdateInvoiceContext
        {
            public UpdateInvoiceContext()
            {

            }

            public Dictionary<BTCPayNetwork, KnownState> KnownStates { get; set; }
            public Dictionary<BTCPayNetwork, KnownState> ModifiedKnownStates { get; set; } = new Dictionary<BTCPayNetwork, KnownState>();
            public InvoiceEntity Invoice { get; set; }
            public List<object> Events { get; set; } = new List<object>();

            bool _Dirty = false;
            public void MarkDirty()
            {
                _Dirty = true;
            }

            public bool Dirty => _Dirty;
        }

        InvoiceRepository _InvoiceRepository;
        EventAggregator _EventAggregator;
        BTCPayWallet _Wallet;
        BTCPayNetworkProvider _NetworkProvider;

        public InvoiceWatcher(
            IHostingEnvironment env,
            BTCPayNetworkProvider networkProvider,
            InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            BTCPayWallet wallet)
        {
            PollInterval = TimeSpan.FromMinutes(1.0);
            _Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _EventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _NetworkProvider = networkProvider;
        }
        CompositeDisposable leases = new CompositeDisposable();

        async Task NotifyReceived(Script scriptPubKey)
        {
            var invoice = await _InvoiceRepository.GetInvoiceIdFromScriptPubKey(scriptPubKey);
            if (invoice != null)
                _WatchRequests.Add(invoice);
        }

        async Task NotifyBlock()
        {
            foreach (var invoice in await _InvoiceRepository.GetPendingInvoices())
            {
                _WatchRequests.Add(invoice);
            }
        }

        private async Task UpdateInvoice(string invoiceId)
        {
            Dictionary<BTCPayNetwork, KnownState> changes = new Dictionary<BTCPayNetwork, KnownState>();
            while (true)
            {
                try
                {
                    var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId, true).ConfigureAwait(false);
                    if (invoice == null)
                        break;
                    var stateBefore = invoice.Status;
                    var updateContext = new UpdateInvoiceContext()
                    {
                        Invoice = invoice,
                        KnownStates = changes
                    };
                    await UpdateInvoice(updateContext).ConfigureAwait(false);
                    if (updateContext.Dirty)
                    {
                        await _InvoiceRepository.UpdateInvoiceStatus(invoice.Id, invoice.Status, invoice.ExceptionStatus).ConfigureAwait(false);
                        _EventAggregator.Publish(new InvoiceDataChangedEvent() { InvoiceId = invoice.Id });
                    }

                    var changed = stateBefore != invoice.Status;

                    foreach (var evt in updateContext.Events)
                    {
                        _EventAggregator.Publish(evt, evt.GetType());
                    }

                    foreach (var modifiedKnownState in updateContext.ModifiedKnownStates)
                    {
                        changes.AddOrReplace(modifiedKnownState.Key, modifiedKnownState.Value);
                    }

                    if (invoice.Status == "complete" ||
                       ((invoice.Status == "invalid" || invoice.Status == "expired") && invoice.MonitoringExpiration < DateTimeOffset.UtcNow))
                    {
                        if (await _InvoiceRepository.RemovePendingInvoice(invoice.Id).ConfigureAwait(false))
                            Logs.PayServer.LogInformation("Stopped watching invoice " + invoiceId);
                        break;
                    }

                    if (!changed || _Cts.Token.IsCancellationRequested)
                        break;
                }
                catch (OperationCanceledException) when (_Cts.Token.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogError(ex, "Unhandled error on watching invoice " + invoiceId);
                    await Task.Delay(10000, _Cts.Token).ConfigureAwait(false);
                }
            }
        }


        private async Task UpdateInvoice(UpdateInvoiceContext context)
        {
            var invoice = context.Invoice;
            //Fetch unknown payments
            var strategies = invoice.GetDerivationStrategies(_NetworkProvider).ToArray();
            var getCoinsResponsesAsync = strategies
                                .Select(d => _Wallet.GetCoins(d, context.KnownStates.TryGet(d.Network), _Cts.Token))
                                .ToArray();
            await Task.WhenAll(getCoinsResponsesAsync);
            var getCoinsResponses = getCoinsResponsesAsync.Select(g => g.Result).ToArray();
            foreach (var response in getCoinsResponses)
            {
                response.Coins = response.Coins.Where(c => invoice.AvailableAddressHashes.Contains(c.ScriptPubKey.Hash.ToString())).ToArray();
            }
            var coins = getCoinsResponses.Where(s => s.Coins.Length != 0).FirstOrDefault();
            bool dirtyAddress = false;
            if (coins != null)
            {
                context.ModifiedKnownStates.Add(coins.Strategy.Network, coins.State);
                var alreadyAccounted = new HashSet<OutPoint>(invoice.Payments.Select(p => p.Outpoint));
                foreach (var coin in coins.Coins.Where(c => !alreadyAccounted.Contains(c.Outpoint)))
                {
                    var payment = await _InvoiceRepository.AddPayment(invoice.Id, coin).ConfigureAwait(false);
                    invoice.Payments.Add(payment);
                    context.Events.Add(new InvoicePaymentEvent(invoice.Id));
                    dirtyAddress = true;
                }
            }
            //////
            var network = coins?.Strategy?.Network ?? _NetworkProvider.GetNetwork(invoice.GetCryptoData().First().Key);
            var cryptoData = invoice.GetCryptoData(network);
            var cryptoDataAll = invoice.GetCryptoData();
            var accounting = cryptoData.Calculate();
            if (invoice.Status == "new" && invoice.ExpirationTime < DateTimeOffset.UtcNow)
            {
                context.MarkDirty();
                await _InvoiceRepository.UnaffectAddress(invoice.Id);

                context.Events.Add(new InvoiceStatusChangedEvent(invoice, "expired"));
                invoice.Status = "expired";
            }

            if (invoice.Status == "new" || invoice.Status == "expired")
            {
                var totalPaid = (await GetPaymentsWithTransaction(network, invoice)).Select(p => p.Payment.GetValue(cryptoDataAll, cryptoData.CryptoCode)).Sum();
                if (totalPaid >= accounting.TotalDue)
                {
                    if (invoice.Status == "new")
                    {
                        context.Events.Add(new InvoiceStatusChangedEvent(invoice, "paid"));
                        invoice.Status = "paid";
                        invoice.ExceptionStatus = null;
                        await _InvoiceRepository.UnaffectAddress(invoice.Id);
                        context.MarkDirty();
                    }
                    else if (invoice.Status == "expired")
                    {
                        invoice.ExceptionStatus = "paidLate";
                        context.MarkDirty();
                    }
                }

                if (totalPaid > accounting.TotalDue && invoice.ExceptionStatus != "paidOver")
                {
                    invoice.ExceptionStatus = "paidOver";
                    await _InvoiceRepository.UnaffectAddress(invoice.Id);
                    context.MarkDirty();
                }

                if (totalPaid < accounting.TotalDue && invoice.Payments.Count != 0 && invoice.ExceptionStatus != "paidPartial")
                {
                    Logs.PayServer.LogInformation("Paid to " + cryptoData.DepositAddress);
                    invoice.ExceptionStatus = "paidPartial";
                    context.MarkDirty();
                    if (dirtyAddress)
                    {
                        var address = await _Wallet.ReserveAddressAsync(coins.Strategy);
                        Logs.PayServer.LogInformation("Generate new " + address);
                        await _InvoiceRepository.NewAddress(invoice.Id, address, network);
                    }
                }
            }

            if (invoice.Status == "paid")
            {
                var transactions = await GetPaymentsWithTransaction(network, invoice);
                if (invoice.SpeedPolicy == SpeedPolicy.HighSpeed)
                {
                    transactions = transactions.Where(t => t.Confirmations >= 1 || !t.Transaction.RBF);
                }
                else if (invoice.SpeedPolicy == SpeedPolicy.MediumSpeed)
                {
                    transactions = transactions.Where(t => t.Confirmations >= 1);
                }
                else if (invoice.SpeedPolicy == SpeedPolicy.LowSpeed)
                {
                    transactions = transactions.Where(t => t.Confirmations >= 6);
                }

                var totalConfirmed = transactions.Select(t => t.Payment.GetValue(cryptoDataAll, cryptoData.CryptoCode)).Sum();

                if (// Is after the monitoring deadline
                   (invoice.MonitoringExpiration < DateTimeOffset.UtcNow)
                   &&
                   // And not enough amount confirmed
                   (totalConfirmed < accounting.TotalDue))
                {
                    await _InvoiceRepository.UnaffectAddress(invoice.Id);
                    context.Events.Add(new InvoiceStatusChangedEvent(invoice, "invalid"));
                    invoice.Status = "invalid";
                    context.MarkDirty();
                }
                else if (totalConfirmed >= accounting.TotalDue)
                {
                    await _InvoiceRepository.UnaffectAddress(invoice.Id);
                    context.Events.Add(new InvoiceStatusChangedEvent(invoice, "confirmed"));
                    invoice.Status = "confirmed";
                    context.MarkDirty();
                }
            }

            if (invoice.Status == "confirmed")
            {
                var transactions = await GetPaymentsWithTransaction(network, invoice);
                transactions = transactions.Where(t => t.Confirmations >= 6);
                var totalConfirmed = transactions.Select(t => t.Payment.GetValue(cryptoDataAll, cryptoData.CryptoCode)).Sum();
                if (totalConfirmed >= accounting.TotalDue)
                {
                    context.Events.Add(new InvoiceStatusChangedEvent(invoice, "complete"));
                    invoice.Status = "complete";
                    context.MarkDirty();
                }
            }
        }

        private async Task<IEnumerable<AccountedPaymentEntity>> GetPaymentsWithTransaction(BTCPayNetwork network, InvoiceEntity invoice)
        {
            var transactions = await _Wallet.GetTransactions(network, invoice.Payments.Select(t => t.Outpoint.Hash).ToArray());

            var spentTxIn = new Dictionary<OutPoint, AccountedPaymentEntity>();
            var result = invoice.Payments.Select(p => p.Outpoint).ToHashSet();
            List<AccountedPaymentEntity> payments = new List<AccountedPaymentEntity>();
            foreach (var payment in invoice.Payments)
            {
                TransactionResult tx;
                if (!transactions.TryGetValue(payment.Outpoint.Hash, out tx))
                {
                    result.Remove(payment.Outpoint);
                    continue;
                }
                AccountedPaymentEntity accountedPayment = new AccountedPaymentEntity()
                {
                    Confirmations = tx.Confirmations,
                    Transaction = tx.Transaction,
                    Payment = payment
                };
                payments.Add(accountedPayment);
                foreach (var txin in tx.Transaction.Inputs)
                {
                    if (!spentTxIn.TryAdd(txin.PrevOut, accountedPayment))
                    {
                        //We get a double spend
                        var existing = spentTxIn[txin.PrevOut];

                        //Take the most recent, the full node is already comparing fees correctly so we have the most likely to be confirmed
                        if (accountedPayment.Confirmations > 1 || existing.Payment.ReceivedTime < accountedPayment.Payment.ReceivedTime)
                        {
                            spentTxIn[txin.PrevOut] = accountedPayment;
                            result.Remove(existing.Payment.Outpoint);
                        }
                    }
                }
            }

            List<PaymentEntity> updated = new List<PaymentEntity>();
            var accountedPayments = payments.Where(p =>
            {
                var accounted = result.Contains(p.Payment.Outpoint);
                if (p.Payment.Accounted != accounted)
                {
                    p.Payment.Accounted = accounted;
                    updated.Add(p.Payment);
                }
                return accounted;
            }).ToArray();

            await _InvoiceRepository.UpdatePayments(payments);
            return accountedPayments;
        }

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
                if (_UpdatePendingInvoices != null)
                {
                    _UpdatePendingInvoices.Change(0, (int)value.TotalMilliseconds);
                }
            }
        }

        private void Watch(string invoiceId)
        {
            if (invoiceId == null)
                throw new ArgumentNullException(nameof(invoiceId));
            _WatchRequests.Add(invoiceId);
        }

        BlockingCollection<string> _WatchRequests = new BlockingCollection<string>(new ConcurrentQueue<string>());

        public void Dispose()
        {
            _Cts.Cancel();
        }


        Thread _Thread;
        TaskCompletionSource<bool> _RunningTask;
        CancellationTokenSource _Cts;
        Timer _UpdatePendingInvoices;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _RunningTask = new TaskCompletionSource<bool>();
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _Thread = new Thread(Run) { Name = "InvoiceWatcher" };
            _Thread.Start();
            _UpdatePendingInvoices = new Timer(async s =>
            {
                foreach (var pending in await _InvoiceRepository.GetPendingInvoices())
                {
                    _WatchRequests.Add(pending);
                }
            }, null, 0, (int)PollInterval.TotalMilliseconds);

            leases.Add(_EventAggregator.Subscribe<Events.NewBlockEvent>(async b => { await NotifyBlock(); }));
            leases.Add(_EventAggregator.Subscribe<Events.TxOutReceivedEvent>(async b => { await NotifyReceived(b.ScriptPubKey); }));
            leases.Add(_EventAggregator.Subscribe<Events.InvoiceCreatedEvent>(b => { Watch(b.InvoiceId); }));

            return Task.CompletedTask;
        }

        void Run()
        {
            Logs.PayServer.LogInformation("Start watching invoices");
            ConcurrentDictionary<string, Lazy<Task>> updating = new ConcurrentDictionary<string, Lazy<Task>>();
            try
            {
                foreach (var item in _WatchRequests.GetConsumingEnumerable(_Cts.Token))
                {
                    try
                    {
                        _Cts.Token.ThrowIfCancellationRequested();
                        var localItem = item;
                        // If the invoice is already updating, ignore
                        Lazy<Task> updateInvoice = new Lazy<Task>(() => UpdateInvoice(localItem), false);
                        if (updating.TryAdd(item, updateInvoice))
                        {
                            updateInvoice.Value.ContinueWith(i => updating.TryRemove(item, out updateInvoice));
                        }
                    }
                    catch (Exception ex) when (!_Cts.Token.IsCancellationRequested)
                    {
                        Logs.PayServer.LogCritical(ex, $"Error in the InvoiceWatcher loop (Invoice {item})");
                        _Cts.Token.WaitHandle.WaitOne(2000);
                    }
                }
            }
            catch (OperationCanceledException)
            {
                try
                {
                    Task.WaitAll(updating.Select(c => c.Value.Value).ToArray());
                }
                catch (AggregateException) { }
                _RunningTask.TrySetResult(true);
            }
            finally
            {
                Logs.PayServer.LogInformation("Stop watching invoices");
            }
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _UpdatePendingInvoices.Dispose();
            _Cts.Cancel();
            return Task.WhenAny(_RunningTask.Task, Task.Delay(-1, cancellationToken));
        }
    }
}
