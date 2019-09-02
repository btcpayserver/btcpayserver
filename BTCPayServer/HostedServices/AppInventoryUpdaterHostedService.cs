using System;
using System.Collections.Generic;
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
                Dictionary<string, int> cartItems = null;
                bool deduct;
                switch (invoiceEvent.Name)
                {
                    case InvoiceEvent.Expired:

                    case InvoiceEvent.MarkedInvalid:
                        deduct = false;
                        break;
                    case InvoiceEvent.Created:
                        deduct = true;
                        break;
                    default:
                        return;
                }

                
                if ((!string.IsNullOrEmpty(invoiceEvent.Invoice.ProductInformation.ItemCode) ||
                     AppService.TryParsePosCartItems(invoiceEvent.Invoice.PosData, out cartItems)))
                {
                    var appIds = AppService.GetAppInternalTags(invoiceEvent.Invoice);

                    if (!appIds.Any())
                    {
                        return;
                    }

                    //get all apps that were tagged that have manageable inventory that has an item that matches the item code in the invoice
                    var apps = (await _AppService.GetApps(appIds)).Select(data =>
                    {
                        switch (Enum.Parse<AppType>(data.AppType))
                        {
                            case AppType.PointOfSale:
                                var possettings = data.GetSettings<AppsController.PointOfSaleSettings>();
                                return (Data: data, Settings: (object)possettings,
                                    Items: _AppService.Parse(possettings.Template, possettings.Currency));
                            case AppType.Crowdfund:
                                var cfsettings = data.GetSettings<CrowdfundSettings>();
                                return (Data: data, Settings: (object)cfsettings,
                                    Items: _AppService.Parse(cfsettings.PerksTemplate, cfsettings.TargetCurrency));
                            default:
                                return (null, null, null);
                        }
                    }).Where(tuple => tuple.Data != null && tuple.Items.Any(item =>
                                          item.Inventory.HasValue &&
                                          ((!string.IsNullOrEmpty(invoiceEvent.Invoice.ProductInformation.ItemCode) &&
                                            item.Id == invoiceEvent.Invoice.ProductInformation.ItemCode) ||
                                           (cartItems != null && cartItems.ContainsKey(item.Id)))));
                    foreach (var valueTuple in apps)
                    {
                        foreach (var item1 in valueTuple.Items.Where(item =>
                            ((!string.IsNullOrEmpty(invoiceEvent.Invoice.ProductInformation.ItemCode) &&
                              item.Id == invoiceEvent.Invoice.ProductInformation.ItemCode) ||
                             (cartItems != null && cartItems.ContainsKey(item.Id)))))
                        {
                            if (cartItems != null && cartItems.ContainsKey(item1.Id))
                            {
                                if (deduct)
                                {
                                    item1.Inventory -= cartItems[item1.Id];
                                }
                                else
                                {
                                    item1.Inventory += cartItems[item1.Id];
                                }
                                
                            }
                            else if (!string.IsNullOrEmpty(invoiceEvent.Invoice.ProductInformation.ItemCode) &&
                                     item1.Id == invoiceEvent.Invoice.ProductInformation.ItemCode)
                            {
                                if (deduct)
                                {
                                    item1.Inventory--;
                                }
                                else
                                {
                                    item1.Inventory++;
                                }
                            }
                        }

                        switch (Enum.Parse<AppType>(valueTuple.Data.AppType))
                        {
                            case AppType.PointOfSale:

                                ((AppsController.PointOfSaleSettings)valueTuple.Settings).Template =
                                    _AppService.SerializeTemplate(valueTuple.Items);
                                break;
                            case AppType.Crowdfund:
                                ((CrowdfundSettings)valueTuple.Settings).PerksTemplate =
                                    _AppService.SerializeTemplate(valueTuple.Items);
                                break;
                            default:
                                throw new InvalidOperationException();
                        }

                        valueTuple.Data.SetSettings(valueTuple.Settings);
                        await _AppService.UpdateOrCreateApp(valueTuple.Data);
                    }
                }
            }
        }
    }
}
