using System;
using System.Collections;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Events;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.Crowdfund;
using BTCPayServer.Plugins.PointOfSale;
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

        public AppInventoryUpdaterHostedService(EventAggregator eventAggregator, AppService appService, Logs logs) : base(eventAggregator, logs)
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
                        switch (data.AppType)
                        {
                            case PointOfSaleAppType.AppType:
                                var possettings = data.GetSettings<PointOfSaleSettings>();
                                return (Data: data, Settings: (object)possettings,
                                    Items: AppService.Parse(possettings.Template));
                            case CrowdfundAppType.AppType:
                                var cfsettings = data.GetSettings<CrowdfundSettings>();
                                return (Data: data, Settings: (object)cfsettings,
                                    Items: AppService.Parse(cfsettings.PerksTemplate));
                            default:
                                return (null, null, null);
                        }
                    }).Where(tuple => tuple.Data != null && tuple.Items.Any(item =>
                                          item.Inventory.HasValue &&
                                          updateAppInventory.Items.FirstOrDefault(i => i.Id == item.Id) != null));
                foreach (var app in apps)
                {
                    foreach (var cartItem in updateAppInventory.Items)
                    {
                        var item = app.Items.FirstOrDefault(item => item.Id == cartItem.Id);
                        if (item == null) continue;
                        
                        if (updateAppInventory.Deduct)
                        {
                            item.Inventory -= cartItem.Count;
                        }
                        else
                        {
                            item.Inventory += cartItem.Count;
                        }
                    }

                    switch (app.Data.AppType)
                    {
                        case PointOfSaleAppType.AppType:
                            ((PointOfSaleSettings)app.Settings).Template =
                                AppService.SerializeTemplate(app.Items);
                            break;
                        case CrowdfundAppType.AppType:
                            ((CrowdfundSettings)app.Settings).PerksTemplate =
                                AppService.SerializeTemplate(app.Items);
                            break;
                        default:
                            throw new InvalidOperationException();
                    }

                    app.Data.SetSettings(app.Settings);
                    await _appService.UpdateOrCreateApp(app.Data);
                }


            }
            else if (evt is InvoiceEvent invoiceEvent)
            {
                List<PosCartItem> cartItems = null;
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

                if (!string.IsNullOrEmpty(invoiceEvent.Invoice.Metadata.ItemCode) ||
                    AppService.TryParsePosCartItems(invoiceEvent.Invoice.Metadata.PosData, out cartItems))
                {
                    var appIds = AppService.GetAppInternalTags(invoiceEvent.Invoice);

                    if (!appIds.Any())
                    {
                        return;
                    }

                    var items = cartItems?.ToList() ?? new List<PosCartItem>();
                    if (!string.IsNullOrEmpty(invoiceEvent.Invoice.Metadata.ItemCode))
                    {
                        items.Add(new PosCartItem
                        {
                            Id = invoiceEvent.Invoice.Metadata.ItemCode,
                            Count = 1,
                            Price = invoiceEvent.Invoice.Price
                        });
                    }

                    _eventAggregator.Publish(new UpdateAppInventory
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
            public List<PosCartItem> Items { get; set; }
            public bool Deduct { get; set; }

            public override string ToString()
            {
                return string.Empty;
            }
        }
    }
}
