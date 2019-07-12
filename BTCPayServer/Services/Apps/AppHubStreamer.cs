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
using BTCPayServer.HostedServices;
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
    public class AppHubStreamer : EventHostedServiceBase
    {
        private readonly AppService _appService;
        private IHubContext<AppHub> _HubContext;

        public AppHubStreamer(EventAggregator eventAggregator,
           IHubContext<AppHub> hubContext,
           AppService appService) : base(eventAggregator)
        {
            _appService = appService;
            _HubContext = hubContext;
        }

        protected override void SubscibeToEvents()
        {
            Subscribe<InvoiceEvent>();
            Subscribe<AppsController.AppUpdated>();
        }

        protected override  async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent)
            {
                foreach (var appId in AppService.GetAppInternalTags(invoiceEvent.Invoice))
                {
                    if (invoiceEvent.Name == InvoiceEvent.ReceivedPayment)
                    {
                        var data = invoiceEvent.Payment.GetCryptoPaymentData();
                        await _HubContext.Clients.Group(appId).SendCoreAsync(AppHub.PaymentReceived, new object[]
                            {
                        data.GetValue(),
                        invoiceEvent.Payment.GetCryptoCode(),
                        invoiceEvent.Payment.GetPaymentMethodId().PaymentType.ToString()
                            }, cancellationToken);
                    }
                    await InfoUpdated(appId);
                }
            }
            else if (evt is AppsController.AppUpdated app)
            {
                await InfoUpdated(app.AppId);
            }
        }

        private async Task InfoUpdated(string appId)
        {
            var info = await _appService.GetAppInfo(appId);
            await _HubContext.Clients.Group(appId).SendCoreAsync(AppHub.InfoUpdated, new object[] { info });
        }
    }
}
