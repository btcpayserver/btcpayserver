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

        async Task NotifyReceived(Script scriptPubKey, BTCPayNetwork network)
        {
            var invoice = await _InvoiceRepository.GetInvoiceIdFromScriptPubKey(scriptPubKey, network.CryptoCode);
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

        private async Task UpdateInvoice(string invoiceId, CancellationToken cancellation)
        {
            Dictionary<BTCPayNetwork, KnownState> changes = new Dictionary<BTCPayNetwork, KnownState>();
            while (!cancellation.IsCancellationRequested)
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

                    if (!changed || cancellation.IsCancellationRequested)
                        break;
                }
                catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                {
                    break;
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogError(ex, "Unhandled error on watching invoice " + invoiceId);
                    await Task.Delay(10000, cancellation).ConfigureAwait(false);
                }
            }
        }


        private async Task UpdateInvoice(UpdateInvoiceContext context)
        {
            var invoice = context.Invoice;
            //Fetch unknown payments
            var strategies = invoice.GetDerivationStrategies(_NetworkProvider).ToArray();
            var getCoinsResponsesAsync = strategies
                                .Select(d => _Wallet.GetCoins(d, context.KnownStates.TryGet(d.Network)))
                                .ToArray();
            await Task.WhenAll(getCoinsResponsesAsync);
            var getCoinsResponses = getCoinsResponsesAsync.Select(g => g.Result).ToArray();
            foreach (var response in getCoinsResponses)
            {
                response.Coins = response.Coins.Where(c => invoice.AvailableAddressHashes.Contains(c.ScriptPubKey.Hash.ToString() + response.Strategy.Network.CryptoCode)).ToArray();
            }
            var coins = getCoinsResponses.Where(s => s.Coins.Length != 0).FirstOrDefault();
            bool dirtyAddress = false;
            if (coins != null)
            {
                if (coins.State != null)
                    context.ModifiedKnownStates.Add(coins.Strategy.Network, coins.State);
                var alreadyAccounted = new HashSet<OutPoint>(invoice.Payments.Select(p => p.Outpoint));
                foreach (var coin in coins.Coins.Where(c => !alreadyAccounted.Contains(c.Outpoint)))
                {
                    var payment = await _InvoiceRepository.AddPayment(invoice.Id, coin, coins.Strategy.Network.CryptoCode).ConfigureAwait(false);
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
            }
        }

        private void Watch(string invoiceId)
        {
            if (invoiceId == null)
                throw new ArgumentNullException(nameof(invoiceId));
            _WatchRequests.Add(invoiceId);
        }

        BlockingCollection<string> _WatchRequests = new BlockingCollection<string>(new ConcurrentQueue<string>());

        Task _Poller;
        Task _Loop;
        CancellationTokenSource _Cts;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            _Poller = StartPoller(_Cts.Token);
            _Loop = StartLoop(_Cts.Token);

            leases.Add(_EventAggregator.Subscribe<Events.NewBlockEvent>(async b => { await NotifyBlock(); }));
            leases.Add(_EventAggregator.Subscribe<Events.TxOutReceivedEvent>(async b => { await NotifyReceived(b.ScriptPubKey, b.Network); }));
            leases.Add(_EventAggregator.Subscribe<Events.InvoiceCreatedEvent>(b => { Watch(b.InvoiceId); }));

            return Task.CompletedTask;
        }


        private async Task StartPoller(CancellationToken cancellation)
        {
            try
            {
                while (!cancellation.IsCancellationRequested)
                {
                    try
                    {
                        foreach (var pending in await _InvoiceRepository.GetPendingInvoices())
                        {
                            _WatchRequests.Add(pending);
                        }
                        await Task.Delay(PollInterval, cancellation);
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        Logs.PayServer.LogError(ex, $"Unhandled exception in InvoiceWatcher poller");
                        await Task.Delay(PollInterval, cancellation);
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested) { }
        }

        async Task StartLoop(CancellationToken cancellation)
        {
            Logs.PayServer.LogInformation("Start watching invoices");
            await Task.Delay(1).ConfigureAwait(false); // Small hack so that the caller does not block on GetConsumingEnumerable
            ConcurrentDictionary<string, Task> executing = new ConcurrentDictionary<string, Task>();
            try
            {
                foreach (var item in _WatchRequests.GetConsumingEnumerable(cancellation))
                {
                    var task = executing.GetOrAdd(item, async i =>
                    {
                        try
                        {
                            await UpdateInvoice(i, cancellation);
                        }
                        catch (Exception ex) when (!cancellation.IsCancellationRequested)
                        {
                            Logs.PayServer.LogCritical(ex, $"Error in the InvoiceWatcher loop (Invoice {item})");
                            await Task.Delay(2000, cancellation);
                        }
                        finally { executing.TryRemove(item, out Task useless); }
                    });
                }
            }
            catch when (cancellation.IsCancellationRequested)
            {
            }
            finally
            {
                await Task.WhenAll(executing.Values);
            }
            Logs.PayServer.LogInformation("Stop watching invoices");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _Cts.Cancel();
            return Task.WhenAll(_Poller, _Loop);
        }
    }
}
