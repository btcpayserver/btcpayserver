﻿using NBXplorer;
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
using BTCPayServer.Services.Wallets;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using Microsoft.AspNetCore.Hosting;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services;

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
        BTCPayNetworkProvider _NetworkProvider;

        public InvoiceWatcher(
            BTCPayNetworkProvider networkProvider,
            InvoiceRepository invoiceRepository,
            EventAggregator eventAggregator)
        {
            PollInterval = TimeSpan.FromMinutes(1.0);
            _InvoiceRepository = invoiceRepository ?? throw new ArgumentNullException(nameof(invoiceRepository));
            _EventAggregator = eventAggregator ?? throw new ArgumentNullException(nameof(eventAggregator));
            _NetworkProvider = networkProvider;
        }
        CompositeDisposable leases = new CompositeDisposable();


        private async Task UpdateInvoice(UpdateInvoiceContext context)
        {
            var invoice = context.Invoice;
            if (invoice.Status == InvoiceStatus.New && invoice.ExpirationTime < DateTimeOffset.UtcNow)
            {
                context.MarkDirty();
                await _InvoiceRepository.UnaffectAddress(invoice.Id);

                context.Events.Add(new InvoiceEvent(invoice.EntityToDTO(_NetworkProvider), 1004, InvoiceEvent.Expired));
                invoice.Status = InvoiceStatus.Expired;
                if(invoice.ExceptionStatus == InvoiceExceptionStatus.PaidPartial)
                    context.Events.Add(new InvoiceEvent(invoice.EntityToDTO(_NetworkProvider), 2000, InvoiceEvent.ExpiredPaidPartial));
            }

            var payments = invoice.GetPayments().Where(p => p.Accounted).ToArray();
            var allPaymentMethods = invoice.GetPaymentMethods(_NetworkProvider);
            var paymentMethod = GetNearestClearedPayment(allPaymentMethods, out var accounting, _NetworkProvider);
            if (paymentMethod == null)
                return;
            var network = _NetworkProvider.GetNetwork(paymentMethod.GetId().CryptoCode);
            if (invoice.Status == InvoiceStatus.New || invoice.Status == InvoiceStatus.Expired)
            {
                if (accounting.Paid >= accounting.MinimumTotalDue)
                {
                    if (invoice.Status == InvoiceStatus.New)
                    {
                        context.Events.Add(new InvoiceEvent(invoice.EntityToDTO(_NetworkProvider), 1003, InvoiceEvent.PaidInFull));
                        invoice.Status = InvoiceStatus.Paid;
                        invoice.ExceptionStatus = accounting.Paid > accounting.TotalDue ? InvoiceExceptionStatus.PaidOver : InvoiceExceptionStatus.None;
                        await _InvoiceRepository.UnaffectAddress(invoice.Id);
                        context.MarkDirty();
                    }
                    else if (invoice.Status == InvoiceStatus.Expired && invoice.ExceptionStatus != InvoiceExceptionStatus.PaidLate)
                    {
                        invoice.ExceptionStatus = InvoiceExceptionStatus.PaidLate;
                        context.Events.Add(new InvoiceEvent(invoice.EntityToDTO(_NetworkProvider), 1009, InvoiceEvent.PaidAfterExpiration));
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
                var confirmedAccounting = paymentMethod.Calculate(p => p.GetCryptoPaymentData().PaymentConfirmed(p, invoice.SpeedPolicy, network));

                if (// Is after the monitoring deadline
                   (invoice.MonitoringExpiration < DateTimeOffset.UtcNow)
                   &&
                   // And not enough amount confirmed
                   (confirmedAccounting.Paid < accounting.MinimumTotalDue))
                {
                    await _InvoiceRepository.UnaffectAddress(invoice.Id);
                    context.Events.Add(new InvoiceEvent(invoice.EntityToDTO(_NetworkProvider), 1013, InvoiceEvent.FailedToConfirm));
                    invoice.Status = InvoiceStatus.Invalid;
                    context.MarkDirty();
                }
                else if (confirmedAccounting.Paid >= accounting.MinimumTotalDue)
                {
                    await _InvoiceRepository.UnaffectAddress(invoice.Id);
                    context.Events.Add(new InvoiceEvent(invoice.EntityToDTO(_NetworkProvider), 1005, InvoiceEvent.Confirmed));
                    invoice.Status = InvoiceStatus.Confirmed;
                    context.MarkDirty();
                }
            }

            if (invoice.Status == InvoiceStatus.Confirmed)
            {
                var completedAccounting = paymentMethod.Calculate(p => p.GetCryptoPaymentData().PaymentCompleted(p, network));
                if (completedAccounting.Paid >= accounting.MinimumTotalDue)
                {
                    context.Events.Add(new InvoiceEvent(invoice.EntityToDTO(_NetworkProvider), 1006, InvoiceEvent.Completed));
                    invoice.Status = InvoiceStatus.Complete;
                    context.MarkDirty();
                }
            }

        }

        public static PaymentMethod GetNearestClearedPayment(PaymentMethodDictionary allPaymentMethods, out PaymentMethodAccounting accounting, BTCPayNetworkProvider networkProvider)
        {
            PaymentMethod result = null;
            accounting = null;
            decimal nearestToZero = 0.0m;
            foreach (var paymentMethod in allPaymentMethods)
            {
                if (networkProvider != null && networkProvider.GetNetwork(paymentMethod.GetId().CryptoCode) == null)
                    continue;
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

        BlockingCollection<string> _WatchRequests = new BlockingCollection<string>(new ConcurrentQueue<string>());

        Task _Loop;
        Task _WaitingInvoices;
        CancellationTokenSource _Cts;

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            _Loop = StartLoop(_Cts.Token);
            _WaitingInvoices = WaitPendingInvoices();

            leases.Add(_EventAggregator.Subscribe<Events.InvoiceNeedUpdateEvent>(b =>
            {
                Watch(b.InvoiceId);
            }));
            leases.Add(_EventAggregator.Subscribe<Events.InvoiceEvent>(async b =>
            {
                if (b.Name == InvoiceEvent.Created)
                {
                    Watch(b.Invoice.Id);
                    await Wait(b.Invoice.Id);
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
            _WaitingInvoices = null;
        }

        async Task StartLoop(CancellationToken cancellation)
        {
            Logs.PayServer.LogInformation("Start watching invoices");
            await Task.Delay(1).ConfigureAwait(false); // Small hack so that the caller does not block on GetConsumingEnumerable
            try
            {
                foreach (var invoiceId in _WatchRequests.GetConsumingEnumerable(cancellation))
                {
                    int maxLoop = 5;
                    int loopCount = -1;
                    while (!cancellation.IsCancellationRequested && loopCount < maxLoop)
                    {
                        loopCount++;
                        try
                        {
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
                                if (await _InvoiceRepository.RemovePendingInvoice(invoice.Id))
                                    _EventAggregator.Publish(new InvoiceStopWatchedEvent(invoice.Id));
                                break;
                            }

                            if (updateContext.Events.Count == 0 || cancellation.IsCancellationRequested)
                                break;
                        }
                        catch (OperationCanceledException) when (cancellation.IsCancellationRequested)
                        {
                            break;
                        }
                        catch (Exception ex)
                        {
                            Logs.PayServer.LogError(ex, "Unhandled error on watching invoice " + invoiceId);
#pragma warning disable CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                            Task.Delay(10000, cancellation)
                                .ContinueWith(t => _WatchRequests.Add(invoiceId), TaskScheduler.Default);
#pragma warning restore CS4014 // Because this call is not awaited, execution of the current method continues before the call is completed
                            break;
                        }
                    }
                }
            }
            catch when (cancellation.IsCancellationRequested)
            {
            }
            Logs.PayServer.LogInformation("Stop watching invoices");
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            leases.Dispose();
            _Cts.Cancel();
            var waitingPendingInvoices = _WaitingInvoices ?? Task.CompletedTask;
            return Task.WhenAll(waitingPendingInvoices, _Loop);
        }
    }
}
