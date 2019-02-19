using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Channels;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.Hubs;
using BTCPayServer.Logging;
using BTCPayServer.Models.AppViewModels;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Apps;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBitcoin;

namespace BTCPayServer.Crowdfund
{
    public class CrowdfundHubStreamer : IHostedService
    {
        private readonly EventAggregator _EventAggregator;
        private readonly IHubContext<CrowdfundHub> _HubContext;

        private List<IEventAggregatorSubscription> _Subscriptions;
        private CancellationTokenSource _Cts;

        public CrowdfundHubStreamer(EventAggregator eventAggregator,
            IHubContext<CrowdfundHub> hubContext)
        {
            _EventAggregator = eventAggregator;
            _HubContext = hubContext;
        }

        private async Task NotifyClients(string appId, InvoiceEvent invoiceEvent, CancellationToken cancellationToken)
        {
            if (invoiceEvent.Name == InvoiceEvent.ReceivedPayment)
            {
                var data = invoiceEvent.Payment.GetCryptoPaymentData();
                await _HubContext.Clients.Group(appId).SendCoreAsync(CrowdfundHub.PaymentReceived, new object[]
                    {
                        data.GetValue(),
                        invoiceEvent.Payment.GetCryptoCode(),
                        Enum.GetName(typeof(PaymentTypes),
                            invoiceEvent.Payment.GetPaymentMethodId().PaymentType)
                    }, cancellationToken);
            }
        }

        Channel<InvoiceEvent> _InvoiceEvents = Channel.CreateUnbounded<InvoiceEvent>();
        public async Task ProcessEvents(CancellationToken cancellationToken)
        {
            while (await _InvoiceEvents.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_InvoiceEvents.Reader.TryRead(out var evt))
                {
                    try
                    {
                        foreach(var appId in AppService.GetAppInternalTags(evt.Invoice.InternalTags))
                            await NotifyClients(appId, evt, cancellationToken);
                    }
                    catch when (cancellationToken.IsCancellationRequested)
                    {
                        throw;
                    }
                    catch (Exception ex)
                    {
                        Logs.PayServer.LogWarning(ex, "Unhandled exception in CrowdfundHubStream");
                    }
                }
            }
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Subscriptions = new List<IEventAggregatorSubscription>()
            {
                _EventAggregator.Subscribe<InvoiceEvent>(e => _InvoiceEvents.Writer.TryWrite(e))
            };
            _Cts = new CancellationTokenSource();
            _ProcessingEvents = ProcessEvents(_Cts.Token);
            return Task.CompletedTask;
        }
        Task _ProcessingEvents = Task.CompletedTask;

        public async Task StopAsync(CancellationToken cancellationToken)
        {
            _Subscriptions?.ForEach(subscription => subscription.Dispose());
            _Cts?.Cancel();
            try
            {
                await _ProcessingEvents;
            }
            catch (OperationCanceledException)
            { }
        }
    }
}
