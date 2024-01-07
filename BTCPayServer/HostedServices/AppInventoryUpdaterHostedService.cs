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
        }

        public AppInventoryUpdaterHostedService(EventAggregator eventAggregator, AppService appService, Logs logs) : base(eventAggregator, logs)
        {
            _eventAggregator = eventAggregator;
            _appService = appService;
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent)
            {
                List<PosCartItem> cartItems = null;
                int deduct;
                switch (invoiceEvent.Name)
                {
                    case InvoiceEvent.Expired:
                    case InvoiceEvent.MarkedInvalid:
                        deduct = 1;
                        break;
                    case InvoiceEvent.Created:
                        deduct = -1;
                        break;
                    default:
                        return;
                }

                if (!string.IsNullOrEmpty(invoiceEvent.Invoice.Metadata.ItemCode) ||
                    AppService.TryParsePosCartItems(invoiceEvent.Invoice.Metadata.PosData, out cartItems))
                {
                    var appIds = AppService.GetAppInternalTags(invoiceEvent.Invoice);

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

                    var changes = items.Select(i => new AppService.InventoryChange(i.Id, i.Count * deduct)).ToArray();
                    foreach (var appId in appIds)
                    {
                        await _appService.UpdateInventory(appId, changes);
                    }
                }
            }
        }
    }
}
