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
        private readonly EventAggregator _eventAggregator;
        private readonly AppService _appService;

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
            Subscribe<UpdateAppInventory>();
        }

        public AppInventoryUpdaterHostedService(EventAggregator eventAggregator, AppService appService) : base(
            eventAggregator)
        {
            _eventAggregator = eventAggregator;
            _appService = appService;
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is UpdateAppInventory updateAppInventory)
            {
                //get all apps that were tagged that have manageable inventory that has an item that matches the item code in the invoice
                var apps = (await _appService.GetApps(updateAppInventory.AppId)).Select(data =>
                    {
                        switch (Enum.Parse<AppType>(data.AppType))
                        {
                            case AppType.PointOfSale:
                                var possettings = data.GetSettings<AppsController.PointOfSaleSettings>();
                                return (Data: data, Settings: (object)possettings,
                                    Items: _appService.Parse(possettings.Template, possettings.Currency));
                            case AppType.Crowdfund:
                                var cfsettings = data.GetSettings<CrowdfundSettings>();
                                return (Data: data, Settings: (object)cfsettings,
                                    Items: _appService.Parse(cfsettings.PerksTemplate, cfsettings.TargetCurrency));
                            default:
                                return (null, null, null);
                        }
                    }).Where(tuple => tuple.Data != null && tuple.Items.Any(item =>
                                          item.Inventory.HasValue &&
                                          updateAppInventory.Items.ContainsKey(item.Id)));
                foreach (var valueTuple in apps)
                {
                    foreach (var item1 in valueTuple.Items.Where(item =>
                        updateAppInventory.Items.ContainsKey(item.Id)))
                    {
                        if (updateAppInventory.Deduct)
                        {
                            item1.Inventory -= updateAppInventory.Items[item1.Id];
                        }
                        else
                        {
                            item1.Inventory += updateAppInventory.Items[item1.Id];
                        }
                    }

                    switch (Enum.Parse<AppType>(valueTuple.Data.AppType))
                    {
                        case AppType.PointOfSale:

                            ((AppsController.PointOfSaleSettings)valueTuple.Settings).Template =
                                _appService.SerializeTemplate(valueTuple.Items);
                            break;
                        case AppType.Crowdfund:
                            ((CrowdfundSettings)valueTuple.Settings).PerksTemplate =
                                _appService.SerializeTemplate(valueTuple.Items);
                            break;
                        default:
                            throw new InvalidOperationException();
                    }

                    valueTuple.Data.SetSettings(valueTuple.Settings);
                    await _appService.UpdateOrCreateApp(valueTuple.Data);
                }


            }
            else if (evt is InvoiceEvent invoiceEvent)
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

                    var items = cartItems ?? new Dictionary<string, int>();
                    if (!string.IsNullOrEmpty(invoiceEvent.Invoice.ProductInformation.ItemCode))
                    {
                        items.TryAdd(invoiceEvent.Invoice.ProductInformation.ItemCode, 1);
                    }

                    _eventAggregator.Publish(new UpdateAppInventory()
                    {
                        Deduct = deduct,
                        Items = items,
                        AppId = appIds
                    });

                }
            }
        }

        public class UpdateAppInventory
        {
            public string[] AppId { get; set; }
            public Dictionary<string, int> Items { get; set; }
            public bool Deduct { get; set; }
        }
    }
}
