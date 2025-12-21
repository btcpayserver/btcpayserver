using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.SignalR;

namespace BTCPayServer.Services.Apps
{
    public class AppHubStreamer : EventHostedServiceBase
    {
        private readonly AppService _appService;
        private readonly PrettyNameProvider _prettyNameProvider;
        private readonly IHubContext<AppHub> _HubContext;

        public AppHubStreamer(EventAggregator eventAggregator,
           IHubContext<AppHub> hubContext,
           AppService appService,
           PrettyNameProvider prettyNameProvider,
           Logs logs) : base(eventAggregator, logs)
        {
            _appService = appService;
            _prettyNameProvider = prettyNameProvider;
            _HubContext = hubContext;
        }

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
            Subscribe<UIAppsController.AppUpdated>();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent)
            {
                foreach (var appId in AppService.GetAppInternalTags(invoiceEvent.Invoice))
                {
                    if (invoiceEvent.Name == InvoiceEvent.ReceivedPayment)
                    {
                        await _HubContext.Clients.Group(appId).SendCoreAsync(AppHub.PaymentReceived, new object[]
                            {
                        invoiceEvent.Payment.Value,
                        invoiceEvent.Payment.Currency,
                        _prettyNameProvider.PrettyName(invoiceEvent.Payment.PaymentMethodId),
                        invoiceEvent.Payment.PaymentMethodId.ToString()
                            }, cancellationToken);
                    }
                    await InfoUpdated(appId);
                }
            }
            else if (evt is UIAppsController.AppUpdated app)
            {
                await InfoUpdated(app.AppId);
            }
        }

        private async Task InfoUpdated(string appId)
        {
            var info = await _appService.GetInfo(appId);
            await _HubContext.Clients.Group(appId).SendCoreAsync(AppHub.InfoUpdated, new object[] { info });
        }
    }
}
