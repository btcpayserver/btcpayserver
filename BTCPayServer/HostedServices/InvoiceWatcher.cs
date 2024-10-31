using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using AngleSharp.Dom;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Notifications;
using BTCPayServer.Services.Notifications.Blobs;
using Microsoft.EntityFrameworkCore;
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

            public bool IsPriceUpdated { get; private set; }
            public void PriceUpdated()
            {
                IsPriceUpdated = true;
            }
        }

        readonly InvoiceRepository _invoiceRepository;
        readonly EventAggregator _eventAggregator;
        readonly ExplorerClientProvider _explorerClientProvider;
        private readonly NotificationSender _notificationSender;
        private readonly PaymentService _paymentService;
        private readonly PaymentMethodHandlerDictionary _handlers;

        public Logs Logs { get; }

        public InvoiceWatcher(
            InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator,
            ExplorerClientProvider explorerClientProvider,
            NotificationSender notificationSender,
            PaymentService paymentService,
            PaymentMethodHandlerDictionary handlers,
            Logs logs)
        {
            _invoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _eventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _explorerClientProvider = explorerClientProvider;
            _notificationSender = notificationSender;
            _paymentService = paymentService;
            _handlers = handlers;
            Logs = logs;
        }

        readonly CompositeDisposable _leases = new();


        private void UpdateInvoice(UpdateInvoiceContext context)
        {
            var invoice = context.Invoice;
            if (invoice.Status == InvoiceStatus.New && invoice.ExpirationTime <= DateTimeOffset.UtcNow)
            {
                context.MarkDirty();
                invoice.Status = InvoiceStatus.Expired;
                var paidPartial = invoice.ExceptionStatus == InvoiceExceptionStatus.PaidPartial;
                context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.Expired) { PaidPartial = paidPartial });
                if (invoice.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)
                    context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.ExpiredPaidPartial) { PaidPartial = paidPartial });
            }

            var hasPayment = invoice.GetPayments(true).Any();
            if (invoice.Status == InvoiceStatus.New || invoice.Status == InvoiceStatus.Expired)
            {
                var isPaid = invoice.IsUnsetTopUp() ?
                    hasPayment :
                    !invoice.IsUnderPaid;
                if (isPaid)
                {
                    if (invoice.Status == InvoiceStatus.New)
                    {
                        context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.PaidInFull));
                        invoice.Status = InvoiceStatus.Processing;
                        if (invoice.IsUnsetTopUp())
                        {
                            invoice.ExceptionStatus = InvoiceExceptionStatus.None;
                            // We know there is at least one payment because hasPayment is true
                            var payment = invoice.GetPayments(true).First();
                            invoice.Price = payment.InvoicePaidAmount.Net;
                            invoice.UpdateTotals();
                            context.PriceUpdated();
                        }
                        else
                        {
                            invoice.ExceptionStatus = invoice.IsOverPaid ? InvoiceExceptionStatus.PaidOver : InvoiceExceptionStatus.None;
                        }
                        context.MarkDirty();
                    }
                    else if (invoice.Status == InvoiceStatus.Expired && invoice.ExceptionStatus != InvoiceExceptionStatus.PaidLate)
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
            if (invoice.Status == InvoiceStatus.Processing)
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
                    invoice.Status = InvoiceStatus.New;
                    invoice.ExceptionStatus = hasPayment ? InvoiceExceptionStatus.PaidPartial : InvoiceExceptionStatus.None;
                    context.MarkDirty();
                }
            }

            if (invoice.Status == InvoiceStatus.Processing)
            {
                var unconfPayments = invoice.GetPayments(false).Where(p => p.Status is PaymentStatus.Processing).ToList();
                var unconfirmedPaid = unconfPayments.Select(p => p.InvoicePaidAmount.Net).Sum();
                var minimumDue = invoice.MinimumNetDue + unconfirmedPaid;
                if (// Is after the monitoring deadline
                   (invoice.MonitoringExpiration < DateTimeOffset.UtcNow)
                   &&
                   // And not enough amount confirmed
                   (minimumDue > 0.0m))
                {
                    context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.FailedToConfirm));
                    invoice.Status = InvoiceStatus.Invalid;
                    context.MarkDirty();
                }
                else if (minimumDue <= 0.0m)
                {
                    invoice.Status = InvoiceStatus.Settled;
                    context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.Confirmed));
                    context.Events.Add(new InvoiceEvent(invoice, InvoiceEvent.Completed));
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

        private async Task Wait(string invoiceId, bool startup) => await Wait(await _invoiceRepository.GetInvoice(invoiceId), startup);
        private async Task Wait(InvoiceEntity invoice, bool startup)
        {
            var startupOffset = TimeSpan.Zero;

            // This give some times for the pollers in the listeners to catch payments which happened
            // while the server was down.
            if (startup)
                startupOffset += TimeSpan.FromMinutes(2.0);

            try
            {
                // add 1 second to ensure watch won't trigger moments before invoice expires
                var delay = (invoice.ExpirationTime.AddSeconds(1) + startupOffset) - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _Cts.Token);
                }
                Watch(invoice.Id);

                // add 1 second to ensure watch won't trigger moments before monitoring expires
                delay = invoice.MonitoringExpiration.AddSeconds(1) - DateTimeOffset.UtcNow;
                if (delay > TimeSpan.Zero)
                {
                    await Task.Delay(delay, _Cts.Token);
                }
                Watch(invoice.Id);
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
                        new InvoiceEventNotification(b.Invoice.Id, b.Name, b.Invoice.StoreId));
                }
                if (b.Name == InvoiceEvent.Created)
                {
                    Watch(b.Invoice.Id);
                    _ = Wait(b.Invoice.Id, false);
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
            await Task.WhenAll((await GetPendingInvoices(_Cts.Token))
                .Select(i => Wait(i, true)).ToArray());
        }

        private async Task<InvoiceEntity[]> GetPendingInvoices(CancellationToken cancellationToken)
        {
            using var ctx = _invoiceRepository.DbContextFactory.CreateContext();
            var rows = await ctx.Invoices.Where(i => Data.InvoiceData.IsPending(i.Status))
                                                .Select(o => o).ToArrayAsync(cancellationToken);
            var invoices = rows.Select(_invoiceRepository.ToEntity).ToArray();
            return invoices;
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
                        if (updateContext.IsPriceUpdated)
                        {
                            await _invoiceRepository.UpdateInvoicePrice(invoice.Id, invoice.Price);
                        }

                        foreach (var evt in updateContext.Events)
                        {
                            _eventAggregator.Publish(evt, evt.GetType());
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
