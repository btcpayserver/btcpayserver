using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;
using NBXplorer;

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
            public List<object> Events { get; set; } = new();

            bool _dirty;

            public void MarkDirty()
            {
                _dirty = true;
            }

            public bool Dirty => _dirty;

            bool _isBlobUpdated;
            public bool IsBlobUpdated => _isBlobUpdated;
            public void BlobUpdated()
            {
                _isBlobUpdated = true;
            }
        }

        readonly InvoiceRepository _invoiceRepository;
        readonly EventAggregator _eventAggregator;
        readonly ExplorerClientProvider _explorerClientProvider;
        private readonly NotificationSender _notificationSender;
        private readonly PaymentService _paymentService;
        public Logs Logs { get; }

        public InvoiceWatcher(
            InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            ExplorerClientProvider explorerClientProvider,
            NotificationSender notificationSender,
            PaymentService paymentService,
            Logs logs)
        {
            _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _explorerClientProvider = explorerClientProvider;
            _notificationSender = notificationSender;
            _paymentService = paymentService;
            Logs = logs;
        }

        readonly CompositeDisposable _leases = new();


        private void UpdateInvoice(UpdateInvoiceContext context)
        {
            var invoice = context.Invoice;
            if (invoice.Status == InvoiceStatusLegacy.New && invoice.ExpirationTime <= DateTimeOffset.UtcNow)
            {
                context.MarkDirty();
                invoice.Status = InvoiceStatusLegacy.Expired;
                var paidPartial = invoice.ExceptionStatus == InvoiceExceptionStatus.PaidPartial;
                context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.Expired) { PaidPartial = paidPartial });
                if (invoice.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)
                    context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.ExpiredPaidPartial) { PaidPartial = paidPartial });
            }

            var hasPayment = invoice.GetPayments(true).Any();
            if (invoice.Status == InvoiceStatusLegacy.New || invoice.Status == InvoiceStatusLegacy.Expired)
            {
                var isPaid = invoice.IsUnsetTopUp() ?
                    hasPayment :
                    !invoice.IsUnderPaid;
                if (isPaid)
                {
                    if (invoice.Status == InvoiceStatusLegacy.New)
                    {
                        context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.PaidInFull));
                        invoice.Status = InvoiceStatusLegacy.Paid;
                        if (invoice.IsUnsetTopUp())
                        {
                            invoice.ExceptionStatus = InvoiceExceptionStatus.None;
                            // We know there is at least one payment because hasPayment is true
                            var payment = invoice.GetPayments(true).First();
                            invoice.Price = payment.InvoicePaidAmount.Net;
                            invoice.UpdateTotals();
                            context.BlobUpdated();
                        }
                        else
                        {
                            invoice.ExceptionStatus = invoice.IsOverPaid ? InvoiceExceptionStatus.PaidOver : InvoiceExceptionStatus.None;
                        }
                        context.MarkDirty();
                    }
                    else if (invoice.Status == InvoiceStatusLegacy.Expired && invoice.ExceptionStatus != InvoiceExceptionStatus.PaidLate)
                    {
                        invoice.ExceptionStatus = InvoiceExceptionStatus.PaidLate;
                        context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.PaidAfterExpiration));
                        context.MarkDirty();
                    }
                }

                if (hasPayment && invoice.IsUnderPaid && invoice.ExceptionStatus != InvoiceExceptionStatus.PaidPartial)
                {
                    invoice.ExceptionStatus = InvoiceExceptionStatus.PaidPartial;
                    context.MarkDirty();
                }
            }

            // Just make sure RBF did not cancelled a payment
            if (invoice.Status == InvoiceStatusLegacy.Paid)
            {
                if (!invoice.IsUnderPaid && !invoice.IsOverPaid && invoice.ExceptionStatus == InvoiceExceptionStatus.PaidOver)
                {
                    invoice.ExceptionStatus = InvoiceExceptionStatus.None;
                    context.MarkDirty();
                }

                if (invoice.IsOverPaid && invoice.ExceptionStatus != InvoiceExceptionStatus.PaidOver)
                {
                    invoice.ExceptionStatus = InvoiceExceptionStatus.PaidOver;
                    context.MarkDirty();
                }

                if (invoice.IsUnderPaid)
                {
                    invoice.Status = InvoiceStatusLegacy.New;
                    invoice.ExceptionStatus = hasPayment ? InvoiceExceptionStatus.PaidPartial : InvoiceExceptionStatus.None;
                    context.MarkDirty();
                }
            }

            if (invoice.Status == InvoiceStatusLegacy.Paid)
            {
                var unconfPayments = invoice.GetPayments(true).Where(p => !p.GetCryptoPaymentData().PaymentConfirmed(p, invoice.SpeedPolicy)).ToList();
                var unconfirmedPaid = unconfPayments.Select(p => p.InvoicePaidAmount.Net).Sum();
                var minimumDue = invoice.MinimumNetDue + unconfirmedPaid;

                if (// Is after the monitoring deadline
                   (invoice.MonitoringExpiration < DateTimeOffset.UtcNow)
                   &&
                   // And not enough amount confirmed
                   (minimumDue > 0.0m))
                {
                    context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.FailedToConfirm));
                    invoice.Status = InvoiceStatusLegacy.Invalid;
                    context.MarkDirty();
                }
                else if (minimumDue <= 0.0m)
                {
                    invoice.Status = InvoiceStatusLegacy.Confirmed;
                    context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.Confirmed));
                    context.MarkDirty();
                }
            }

            if (invoice.Status == InvoiceStatusLegacy.Confirmed)
            {
                var unconfPayments = invoice.GetPayments(true).Where(p => !p.GetCryptoPaymentData().PaymentCompleted(p)).ToList();
                var unconfirmedPaid = unconfPayments.Select(p => p.InvoicePaidAmount.Net).Sum();
                var minimumDue = invoice.MinimumNetDue + unconfirmedPaid;

                if (minimumDue <= 0.0m)
                {
                    context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.Completed));
                    invoice.Status = InvoiceStatusLegacy.Complete;
                    context.MarkDirty();
                }
            }

        }

        private void Watch(string invoiceId)
        {
            ArgumentNullException.ThrowIfNull(invoiceId);

            if (!_WatchRequests.Writer.TryWrite(invoiceId))
            {
                Logs.PayServer.LogWarning($"Failed to write invoice {invoiceId} into WatchRequests channel");
            }
        }

        private async Task Wait(string invoiceId)
        {
            var invoice = await _invoiceRepository.GetInvoice(invoiceId);
            try
            {
                // add 1 second to ensure watch won't trigger moments before invoice expires
                var delay = invoice.ExpirationTime.AddSeconds(1) - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _Cts.Token);
                }
                Watch(invoiceId);

                // add 1 second to ensure watch won't trigger moments before monitoring expires
                delay = invoice.MonitoringExpiration.AddSeconds(1) - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _Cts.Token);
                }
                Watch(invoiceId);
            }
            catch when (_Cts.IsCancellationRequested)
            { }

        }

        readonly Channel<string> _WatchRequests = Channel.CreateUnbounded<string>();

        Task _Loop;
        CancellationTokenSource _Cts;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _Loop = StartLoop(_Cts.Token);
            _ = WaitPendingInvoices();

            _leases.Add(_eventAggregator.Subscribe<Events.InvoiceNeedUpdateEvent>(b =>
            {
                Watch(b.InvoiceId);
            }));
            _leases.Add(_eventAggregator.SubscribeAsync<Events.InvoiceEvent>(async b =>
            {
                if (InvoiceEventNotification.HandlesEvent(b.Name))
                {
                    await _notificationSender.SendNotification(new StoreScope(b.Invoice.StoreId),
                        new InvoiceEventNotification(b.Invoice.Id, b.Name));
                }
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
            await Task.WhenAll((await _invoiceRepository.GetPendingInvoiceIds())
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
                        var invoice = await _invoiceRepository.GetInvoice(invoiceId, true);
                        if (invoice == null)
                            break;
                        var updateContext = new UpdateInvoiceContext(invoice);
                        UpdateInvoice(updateContext);
                        if (updateContext.Dirty)
                        {
                            await _invoiceRepository.UpdateInvoiceStatus(invoice.Id, invoice.GetInvoiceState());
                            updateContext.Events.Insert(0, new InvoiceDataChangedEvent(invoice));
                        }
                        if (updateContext.IsBlobUpdated)
                        {
                            await _invoiceRepository.UpdateInvoicePrice(invoice.Id, invoice);
                        }

                        foreach (var evt in updateContext.Events)
                        {
                            _eventAggregator.Publish(evt, evt.GetType());
                        }

                        if (invoice.Status == InvoiceStatusLegacy.Complete ||
                           ((invoice.Status == InvoiceStatusLegacy.Invalid || invoice.Status == InvoiceStatusLegacy.Expired) && invoice.MonitoringExpiration < DateTimeOffset.UtcNow))
                        {
                            var extendInvoiceMonitoring = await UpdateConfirmationCount(invoice);

                            // we extend monitor time if we haven't reached max confirmation count
                            // say user used low fee and we only got 3 confirmations right before it's time to remove
                            if (extendInvoiceMonitoring)
                            {
                                await _invoiceRepository.ExtendInvoiceMonitor(invoice.Id);
                            }
                            else if (await _invoiceRepository.RemovePendingInvoice(invoice.Id))
                            {
                                _eventAggregator.Publish(new InvoiceStopWatchedEvent(invoice.Id));
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
                .GetPayments(false)
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
                            var client = _explorerClientProvider.GetExplorerClient(payment.Currency);
                            var transactionResult = client is null ? null : await client.GetTransactionAsync(onChainPaymentData.Outpoint.Hash);
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
                await _paymentService.UpdatePayments(updatedPaymentData);
            }

            return extendInvoiceMonitoring;
        }

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            if (_Cts == null)
                return;
            _leases.Dispose();
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
