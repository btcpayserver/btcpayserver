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

namespace BTCPayServer.Services.Apps
{
    public class AppHubStreamer : IHostedService
    {
        private readonly EventAggregator _EventAggregator;
        private readonly IHubContext<AppHub> _HubContext;
        private readonly AppService _appService;
        private List<IEventAggregatorSubscription> _Subscriptions;
        private CancellationTokenSource _Cts;

        public AppHubStreamer(EventAggregator eventAggregator,
            IHubContext<AppHub> hubContext,
            AppService appService)
        {
            _EventAggregator = eventAggregator;
            _HubContext = hubContext;
            _appService = appService;
        }

        private async Task NotifyClients(string appId, InvoiceEvent invoiceEvent, CancellationToken cancellationToken)
        {
            if (invoiceEvent.Name == InvoiceEvent.ReceivedPayment)
            {
                var data = invoiceEvent.Payment.GetCryptoPaymentData();
                await _HubContext.Clients.Group(appId).SendCoreAsync(AppHub.PaymentReceived, new object[]
                    {
                        data.GetValue(),
                        invoiceEvent.Payment.GetCryptoCode(),
                        Enum.GetName(typeof(PaymentTypes),
                            invoiceEvent.Payment.GetPaymentMethodId().PaymentType)
                    }, cancellationToken);
            }
            await InfoUpdated(appId);
        }

        Channel<object> _Events = Channel.CreateUnbounded<object>();
        public async Task ProcessEvents(CancellationToken cancellationToken)
        {
            while (await _Events.Reader.WaitToReadAsync(cancellationToken))
            {
                if (_Events.Reader.TryRead(out var evt))
                {
                    try
                    {
                        if (evt is InvoiceEvent invoiceEvent)
                        {
                            foreach (var appId in AppService.GetAppInternalTags(invoiceEvent.Invoice.InternalTags))
                                await NotifyClients(appId, invoiceEvent, cancellationToken);
                        }
                        else if (evt is AppsController.AppUpdated app)
                        {
                            await InfoUpdated(app.AppId);
                        }
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

        private async Task InfoUpdated(string appId)
        {
            var info = await _appService.GetAppInfo(appId);
            await _HubContext.Clients.Group(appId).SendCoreAsync(AppHub.InfoUpdated, new object[] { info });
        }

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Subscriptions = new List<IEventAggregatorSubscription>()
            {
                _EventAggregator.Subscribe<InvoiceEvent>(e => _Events.Writer.TryWrite(e)),
                _EventAggregator.Subscribe<AppsController.AppUpdated>(e => _Events.Writer.TryWrite(e))
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
