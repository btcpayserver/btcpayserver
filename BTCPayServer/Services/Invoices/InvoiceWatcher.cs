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

namespace BTCPayServer.Services.Invoices
{
    public class InvoiceWatcherAccessor
    {
        public InvoiceWatcher Instance { get; set; }
    }
    public class InvoiceWatcher : IHostedService
    {
        InvoiceRepository _InvoiceRepository;
        ExplorerClient _ExplorerClient;
        DerivationStrategyFactory _DerivationFactory;
        EventAggregator _EventAggregator;
        BTCPayWallet _Wallet;
        

        public InvoiceWatcher(ExplorerClient explorerClient,
            InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            BTCPayWallet wallet,
            InvoiceWatcherAccessor accessor)
        {
            LongPollingMode = explorerClient.Network == Network.RegTest;
            PollInterval = explorerClient.Network == Network.RegTest ? TimeSpan.FromSeconds(10.0) : TimeSpan.FromMinutes(1.0);
            _Wallet = wallet ?? throw new ArgumentNullException(nameof(wallet));
            _ExplorerClient = explorerClient ?? throw new ArgumentNullException(nameof(explorerClient));
            _DerivationFactory = new DerivationStrategyFactory(_ExplorerClient.Network);
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _EventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            accessor.Instance = this;
        }
        CompositeDisposable leases = new CompositeDisposable();

        public bool LongPollingMode
        {
            get; set;
        }

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
            UTXOChanges changes = null;
            while (true)
            {
                try
                {
                    var invoice = await _InvoiceRepository.GetInvoice(null, invoiceId, true).ConfigureAwait(false);
                    if (invoice == null)
                        break;
                    var stateBefore = invoice.Status;
                    var stateChanges = new List<string>();
                    var result = await UpdateInvoice(changes, invoice, stateChanges).ConfigureAwait(false);
                    changes = result.Changes;
                    if (result.NeedSave)
                    { 
                        await _InvoiceRepository.UpdateInvoiceStatus(invoice.Id, invoice.Status, invoice.ExceptionStatus).ConfigureAwait(false);
                        _EventAggregator.Publish(new InvoiceDataChangedEvent() { InvoiceId = invoice.Id });
                    }

                    var changed = stateBefore != invoice.Status;

                    foreach(var stateChange in stateChanges)
                    {
                        _EventAggregator.Publish(new InvoiceStatusChangedEvent() { InvoiceId = invoice.Id, NewState = stateChange, OldState = stateBefore });
                        stateBefore = stateChange;
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


        private async Task<(bool NeedSave, UTXOChanges Changes)> UpdateInvoice(UTXOChanges changes, InvoiceEntity invoice, List<string> stateChanges)
        {
            bool needSave = false;
            //Fetch unknown payments
            var strategy = _DerivationFactory.Parse(invoice.DerivationStrategy);
            changes = await _ExplorerClient.SyncAsync(strategy, changes, !LongPollingMode, _Cts.Token).ConfigureAwait(false);

            var utxos = changes.Confirmed.UTXOs.Concat(changes.Unconfirmed.UTXOs).ToArray();
            List<Coin> receivedCoins = new List<Coin>();
            foreach (var received in utxos)
                if (invoice.AvailableAddressHashes.Contains(received.Output.ScriptPubKey.Hash.ToString()))
                    receivedCoins.Add(new Coin(received.Outpoint, received.Output));

            var alreadyAccounted = new HashSet<OutPoint>(invoice.Payments.Select(p => p.Outpoint));
            bool dirtyAddress = false;
            foreach (var coin in receivedCoins.Where(c => !alreadyAccounted.Contains(c.Outpoint)))
            {
                var payment = await _InvoiceRepository.AddPayment(invoice.Id, coin).ConfigureAwait(false);
                invoice.Payments.Add(payment);
                dirtyAddress = true;
            }
            //////

            if (invoice.Status == "new" && invoice.ExpirationTime < DateTimeOffset.UtcNow)
            {
                needSave = true;
                await _InvoiceRepository.UnaffectAddress(invoice.Id);
                invoice.Status = "expired";
                stateChanges.Add(invoice.Status);
            }

            if (invoice.Status == "new" || invoice.Status == "expired")
            {
                var totalPaid = (await GetPaymentsWithTransaction(invoice)).Select(p => p.Payment.Output.Value).Sum();
                if (totalPaid >= invoice.GetTotalCryptoDue())
                {
                    if (invoice.Status == "new")
                    {
                        invoice.Status = "paid";
                        stateChanges.Add(invoice.Status);
                        invoice.ExceptionStatus = null;
                        await _InvoiceRepository.UnaffectAddress(invoice.Id);
                        needSave = true;
                    }
                    else if (invoice.Status == "expired")
                    {
                        invoice.ExceptionStatus = "paidLate";
                        needSave = true;
                    }
                }

                if (totalPaid > invoice.GetTotalCryptoDue() && invoice.ExceptionStatus != "paidOver")
                {
                    invoice.ExceptionStatus = "paidOver";
                    await _InvoiceRepository.UnaffectAddress(invoice.Id);
                    needSave = true;
                }

                if (totalPaid < invoice.GetTotalCryptoDue() && invoice.Payments.Count != 0 && invoice.ExceptionStatus != "paidPartial")
                {
                    Logs.PayServer.LogInformation("Paid to " + invoice.DepositAddress);
                    invoice.ExceptionStatus = "paidPartial";
                    needSave = true;
                    if (dirtyAddress)
                    {
                        var address = await _Wallet.ReserveAddressAsync(_DerivationFactory.Parse(invoice.DerivationStrategy));
                        Logs.PayServer.LogInformation("Generate new " + address);
                        await _InvoiceRepository.NewAddress(invoice.Id, address);
                    }
                }
            }

            if (invoice.Status == "paid")
            {
                var transactions = await GetPaymentsWithTransaction(invoice);
                var chainConfirmedTransactions = transactions.Where(t => t.Confirmations >= 1);
                if (invoice.SpeedPolicy == SpeedPolicy.HighSpeed)
                {
                    transactions = transactions.Where(t => !t.Transaction.RBF);
                }
                else if (invoice.SpeedPolicy == SpeedPolicy.MediumSpeed)
                {
                    transactions = transactions.Where(t => t.Confirmations >= 1);
                }
                else if (invoice.SpeedPolicy == SpeedPolicy.LowSpeed)
                {
                    transactions = transactions.Where(t => t.Confirmations >= 6);
                }

                var chainTotalConfirmed = chainConfirmedTransactions.Select(t => t.Payment.Output.Value).Sum();

                if (// Is after the monitoring deadline
                   (invoice.MonitoringExpiration < DateTimeOffset.UtcNow)
                   &&
                   // And not enough amount confirmed
                   (chainTotalConfirmed < invoice.GetTotalCryptoDue()))
                {
                    await _InvoiceRepository.UnaffectAddress(invoice.Id);
                    invoice.Status = "invalid";
                    stateChanges.Add(invoice.Status);
                    needSave = true;
                }
                else
                {
                    var totalConfirmed = transactions.Select(t => t.Payment.Output.Value).Sum();
                    if (totalConfirmed >= invoice.GetTotalCryptoDue())
                    {
                        await _InvoiceRepository.UnaffectAddress(invoice.Id);
                        invoice.Status = "confirmed";
                        stateChanges.Add(invoice.Status);
                        needSave = true;
                    }
                }
            }

            if (invoice.Status == "confirmed")
            {
                var transactions = await GetPaymentsWithTransaction(invoice);
                transactions = transactions.Where(t => t.Confirmations >= 6);
                var totalConfirmed = transactions.Select(t => t.Payment.Output.Value).Sum();
                if (totalConfirmed >= invoice.GetTotalCryptoDue())
                {
                    invoice.Status = "complete";
                    stateChanges.Add(invoice.Status);
                    needSave = true;
                }
            }
            return (needSave, changes);
        }

        private async Task<IEnumerable<AccountedPaymentEntity>> GetPaymentsWithTransaction(InvoiceEntity invoice)
        {
            var transactions = await _ExplorerClient.GetTransactions(invoice.Payments.Select(t => t.Outpoint.Hash).ToArray());

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

        public void Watch(string invoiceId)
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

            leases.Add(_EventAggregator.Subscribe<NewBlockEvent>(async b => { await NotifyBlock(); }));
            leases.Add(_EventAggregator.Subscribe<TxOutReceivedEvent>(async b => { await NotifyReceived(b.ScriptPubKey); }));

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
