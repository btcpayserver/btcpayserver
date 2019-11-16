using NBXplorer;
using Microsoft.Extensions.Logging;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using BTCPayServer.Logging;
using System.Threading;
using Microsoft.Extensions.Hosting;
using System.Collections.Concurrent;
using BTCPayServer.Events;
using BTCPayServer.Services.Invoices;
using System.Threading.Channels;

namespace BTCPayServer.HostedServices
{
    public class InvoiceWatcher : IHostedService
    {
        class UpdateInvoiceContext
        {
            public UpdateInvoiceContext(InvoiceEntity invoice)
            {
                Invoice = invoice;
            }
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
        ExplorerClientProvider _ExplorerClientProvider;

        public InvoiceWatcher(
            InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            ExplorerClientProvider explorerClientProvider)
        {
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _EventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _ExplorerClientProvider = explorerClientProvider;
        }
        CompositeDisposable leases = new CompositeDisposable();


        private async Task UpdateInvoice(UpdateInvoiceContext context)
        {
            var invoice = context.Invoice;
            if (invoice.Status == InvoiceStatus.New && invoice.ExpirationTime < DateTimeOffset.UtcNow)
            {
                context.MarkDirty();
                await _InvoiceRepository.UnaffectAddress(invoice.Id);

                invoice.Status = InvoiceStatus.Expired;
                context.Events.Add(new InvoiceEvent(invoice, 1004, InvoiceEvent.Expired));
                if (invoice.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)
                    context.Events.Add(new InvoiceEvent(invoice, 2000, InvoiceEvent.ExpiredPaidPartial));
            }

            var payments = invoice.GetPayments().Where(p => p.Accounted).ToArray();
            var allPaymentMethods = invoice.GetPaymentMethods();
            var paymentMethod = GetNearestClearedPayment(allPaymentMethods, out var accounting);
            if (paymentMethod == null)
                return;
            if (invoice.Status == InvoiceStatus.New || invoice.Status == InvoiceStatus.Expired)
            {
                if (accounting.Paid >= accounting.MinimumTotalDue)
                {
                    if (invoice.Status == InvoiceStatus.New)
                    {
                        context.Events.Add(new InvoiceEvent(invoice, 1003, InvoiceEvent.PaidInFull));
                        invoice.Status = InvoiceStatus.Paid;
                        invoice.ExceptionStatus = accounting.Paid > accounting.TotalDue ? InvoiceExceptionStatus.PaidOver : InvoiceExceptionStatus.None;
                        await _InvoiceRepository.UnaffectAddress(invoice.Id);
                        context.MarkDirty();
                    }
                    else if (invoice.Status == InvoiceStatus.Expired && invoice.ExceptionStatus != InvoiceExceptionStatus.PaidLate)
                    {
                        invoice.ExceptionStatus = InvoiceExceptionStatus.PaidLate;
                        context.Events.Add(new InvoiceEvent(invoice, 1009, InvoiceEvent.PaidAfterExpiration));
                        context.MarkDirty();
                    }
                }

                if (accounting.Paid < accounting.MinimumTotalDue && invoice.GetPayments().Count != 0 && invoice.ExceptionStatus != InvoiceExceptionStatus.PaidPartial)
                {
                    invoice.ExceptionStatus = InvoiceExceptionStatus.PaidPartial;
                    context.MarkDirty();
                }
            }

            // Just make sure RBF did not cancelled a payment
            if (invoice.Status == InvoiceStatus.Paid)
            {
                if (accounting.MinimumTotalDue <= accounting.Paid && accounting.Paid <= accounting.TotalDue && invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver)
                {
                    invoice.ExceptionStatus = InvoiceExceptionStatus.None;
                    context.MarkDirty();
                }

                if (accounting.Paid > accounting.TotalDue && invoice.ExceptionStatus != InvoiceExceptionStatus.PaidOver)
                {
                    invoice.ExceptionStatus = InvoiceExceptionStatus.PaidOver;
                    context.MarkDirty();
                }

                if (accounting.Paid < accounting.MinimumTotalDue)
                {
                    invoice.Status = InvoiceStatus.New;
                    invoice.ExceptionStatus = accounting.Paid == Money.Zero ? InvoiceExceptionStatus.None : InvoiceExceptionStatus.PaidPartial;
                    context.MarkDirty();
                }
            }

            if (invoice.Status == InvoiceStatus.Paid)
            {
                var confirmedAccounting = paymentMethod.Calculate(p => p.GetCryptoPaymentData().PaymentConfirmed(p, invoice.SpeedPolicy));

                if (// Is after the monitoring deadline
                   (invoice.MonitoringExpiration < DateTimeOffset.UtcNow)
                   &&
                   // And not enough amount confirmed
                   (confirmedAccounting.Paid < accounting.MinimumTotalDue))
                {
                    await _InvoiceRepository.UnaffectAddress(invoice.Id);
                    context.Events.Add(new InvoiceEvent(invoice, 1013, InvoiceEvent.FailedToConfirm));
                    invoice.Status = InvoiceStatus.Invalid;
                    context.MarkDirty();
                }
                else if (confirmedAccounting.Paid >= accounting.MinimumTotalDue)
                {
                    await _InvoiceRepository.UnaffectAddress(invoice.Id);
                    invoice.Status = InvoiceStatus.Confirmed;
                    context.Events.Add(new InvoiceEvent(invoice, 1005, InvoiceEvent.Confirmed));
                    context.MarkDirty();
                }
            }

            if (invoice.Status == InvoiceStatus.Confirmed)
            {
                var completedAccounting = paymentMethod.Calculate(p => p.GetCryptoPaymentData().PaymentCompleted(p));
                if (completedAccounting.Paid >= accounting.MinimumTotalDue)
                {
                    context.Events.Add(new InvoiceEvent(invoice, 1006, InvoiceEvent.Completed));
                    invoice.Status = InvoiceStatus.Complete;
                    context.MarkDirty();
                }
            }

        }

        public static PaymentMethod GetNearestClearedPayment(PaymentMethodDictionary allPaymentMethods, out PaymentMethodAccounting accounting)
        {
            PaymentMethod result = null;
            accounting = null;
            decimal nearestToZero = 0.0m;
            foreach (var paymentMethod in allPaymentMethods)
            {
                var currentAccounting = paymentMethod.Calculate();
                var distanceFromZero = Math.Abs(currentAccounting.DueUncapped.ToDecimal(MoneyUnit.BTC));
                if (result == null || distanceFromZero < nearestToZero)
                {
                    result = paymentMethod;
                    nearestToZero = distanceFromZero;
                    accounting = currentAccounting;
                }
            }
            return result;
        }

        private void Watch(string invoiceId)
        {
            if (invoiceId == null)
                throw new ArgumentNullException(nameof(invoiceId));
            _WatchRequests.Writer.TryWrite(invoiceId);
        }

        private async Task Wait(string invoiceId)
        {
            var invoice = await _InvoiceRepository.GetInvoice(invoiceId);
            try
            {
                var delay = invoice.ExpirationTime - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _Cts.Token);
                }
                Watch(invoiceId);
                delay = invoice.MonitoringExpiration - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _Cts.Token);
                }
                Watch(invoiceId);
            }
            catch when (_Cts.IsCancellationRequested)
            { }

        }

        Channel<string> _WatchRequests = Channel.CreateUnbounded<string>();

        Task _Loop;
        CancellationTokenSource _Cts;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _Loop = StartLoop(_Cts.Token);
            _ = WaitPendingInvoices();

            leases.Add(_EventAggregator.Subscribe<Events.InvoiceNeedUpdateEvent>(b =>
            {
                Watch(b.InvoiceId);
            }));
            leases.Add(_EventAggregator.Subscribe<Events.InvoiceEvent>(b =>
            {
                if (b.Name == InvoiceEvent.Created)
                {
                    Watch(b.Invoice.Id);
                    _ = Wait(b.Invoice.Id);
                }

                if (b.Name == InvoiceEvent.ReceivedPayment)
                {
                    Watch(b.Invoice.Id);
                }
            }));
            return Task.CompletedTask;
        }

        private async Task WaitPendingInvoices()
        {
            await Task.WhenAll((await _InvoiceRepository.GetPendingInvoices())
                .Select(id => Wait(id)).ToArray());
        }

        async Task StartLoop(CancellationToken cancellation)
        {
            Logs.PayServer.LogInformation("Start watching invoices");
            while (await _WatchRequests.Reader.WaitToReadAsync(cancellation) && _WatchRequests.Reader.TryRead(out var invoiceId))
            {
                int maxLoop = 5;
                int loopCount = -1;
                while (loopCount < maxLoop)
                {
                    loopCount++;
                    try
                    {
                        cancellation.ThrowIfCancellationRequested();
                        var invoice = await _InvoiceRepository.GetInvoice(invoiceId, true);
                        if (invoice == null)
                            break;
                        var updateContext = new UpdateInvoiceContext(invoice);
                        await UpdateInvoice(updateContext);
                        if (updateContext.Dirty)
                        {
                            await _InvoiceRepository.UpdateInvoiceStatus(invoice.Id, invoice.GetInvoiceState());
                            updateContext.Events.Insert(0, new InvoiceDataChangedEvent(invoice));
                        }

                        foreach (var evt in updateContext.Events)
                        {
                            _EventAggregator.Publish(evt, evt.GetType());
                        }

                        if (invoice.Status == InvoiceStatus.Complete ||
                           ((invoice.Status == InvoiceStatus.Invalid || invoice.Status == InvoiceStatus.Expired) && invoice.MonitoringExpiration < DateTimeOffset.UtcNow))
                        {
                            var extendInvoiceMonitoring = await UpdateConfirmationCount(invoice);

                            // we extend monitor time if we haven't reached max confirmation count
                            // say user used low fee and we only got 3 confirmations right before it's time to remove
                            if (extendInvoiceMonitoring)
                            {
                                await _InvoiceRepository.ExtendInvoiceMonitor(invoice.Id);
                            }
                            else if (await _InvoiceRepository.RemovePendingInvoice(invoice.Id))
                            {
                                _EventAggregator.Publish(new InvoiceStopWatchedEvent(invoice.Id));
                            }
                            break;
                        }

                        if (updateContext.Events.Count == 0)
                            break;
                    }
                    catch (Exception ex) when (!cancellation.IsCancellationRequested)
                    {
                        Logs.PayServer.LogError(ex, "Unhandled error on watching invoice " + invoiceId);
                        _ = Task.Delay(10000, cancellation)
                            .ContinueWith(t => Watch(invoiceId), TaskScheduler.Default);
                        break;
                    }
                }
            }
        }

        private async Task<bool> UpdateConfirmationCount(InvoiceEntity invoice)
        {
            bool extendInvoiceMonitoring = false;
            var updateConfirmationCountIfNeeded = invoice
                .GetPayments()
                .Select<PaymentEntity, Task<PaymentEntity>>(async payment =>
                {
                    var paymentData = payment.GetCryptoPaymentData();
                    if (paymentData is Payments.Bitcoin.BitcoinLikePaymentData onChainPaymentData)
                    {
                        var network = payment.Network as BTCPayNetwork;
                        // Do update if confirmation count in the paymentData is not up to date
                        if ((onChainPaymentData.ConfirmationCount < network.MaxTrackedConfirmation && payment.Accounted)
                            && (onChainPaymentData.Legacy || invoice.MonitoringExpiration < DateTimeOffset.UtcNow))
                        {
                            var transactionResult = await _ExplorerClientProvider.GetExplorerClient(payment.GetCryptoCode())?.GetTransactionAsync(onChainPaymentData.Outpoint.Hash);
                            var confirmationCount = transactionResult?.Confirmations ?? 0;
                            onChainPaymentData.ConfirmationCount = confirmationCount;
                            payment.SetCryptoPaymentData(onChainPaymentData);

                            // we want to extend invoice monitoring until we reach max confirmations on all onchain payment methods
                            if (confirmationCount < network.MaxTrackedConfirmation)
                                extendInvoiceMonitoring = true;
                            
                            return payment;
                        }
                    }
                    return null;
                })
                .ToArray();
            await Task.WhenAll(updateConfirmationCountIfNeeded);
            var updatedPaymentData = updateConfirmationCountIfNeeded.Where(a => a.Result != null).Select(a => a.Result).ToList();
            if (updatedPaymentData.Count > 0)
            {
                await _InvoiceRepository.UpdatePayments(updatedPaymentData);
            }

            return extendInvoiceMonitoring;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_Cts == null)
                return;
            leases.Dispose();
            _Cts.Cancel();
            try
            {
                await _Loop;
            }
            catch { }
            finally
            {
                Logs.PayServer.LogInformation("Stop watching invoices");
            }
        }
    }
}
