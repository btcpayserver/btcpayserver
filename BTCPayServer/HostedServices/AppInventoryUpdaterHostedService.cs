using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.HostedServices
{
    public class AppInventoryUpdaterHostedService : EventHostedServiceBase
    {
        private readonly AppService _AppService;

        protected override void SubscibeToEvents()
        {
            Subscribe<InvoiceEvent>();
        }


        public AppInventoryUpdaterHostedService(EventAggregator eventAggregator, AppService appService) : base(
            eventAggregator)
        {
            _AppService = appService;
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent)
            {
                if (new[] {InvoiceEvent.Expired, InvoiceEvent.MarkedInvalid}.Contains(invoiceEvent.Name) &&
                    !string.IsNullOrEmpty(invoiceEvent.Invoice.ProductInformation.ItemCode))
                {
                    var appIds = AppService.GetAppInternalTags(invoiceEvent.Invoice);

                    if (!appIds.Any())
                    {
                        return;
                    }

                    var apps = await _AppService.GetApps(appIds);
                    var pos = apps.Where(data => data.AppType == AppType.PointOfSale.ToString()).Select(data =>
                    {
                        var settings = data.GetSettings<AppsController.PointOfSaleSettings>();
                        return (Data: data, Settings: settings,
                            Items: _AppService.Parse(settings.Template, settings.Currency));
                    }).Where(tuple => tuple.Items.Any(item =>
                        item.Inventory > 0 && item.Id == invoiceEvent.Invoice.ProductInformation.ItemCode));
                    foreach (var valueTuple in pos)
                    {
                        foreach (var item1 in valueTuple.Items.Where(item =>  item.Id == invoiceEvent.Invoice.ProductInformation.ItemCode))
                        {
                            item1.Inventory++;
                        }

                        valueTuple.Settings.Template = _AppService.SerializeTemplate(valueTuple.Items);
                        valueTuple.Data.SetSettings(valueTuple.Settings);
                        await _AppService.UpdateAppSettings(valueTuple.Data);
                    }
                }
            }
        }
    }
}
