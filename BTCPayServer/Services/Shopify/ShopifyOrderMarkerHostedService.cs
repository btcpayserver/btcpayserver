using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Services.Shopify.Models;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Services.Shopify
{
    public class ShopifyOrderMarkerHostedService : EventHostedServiceBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        public ShopifyOrderMarkerHostedService(EventAggregator eventAggregator,
            StoreRepository storeRepository,
            IHttpClientFactory httpClientFactory) : base(eventAggregator)
        {
            _storeRepository = storeRepository;
            _httpClientFactory = httpClientFactory;
        }

        public const string SHOPIFY_ORDER_ID_PREFIX = "shopify-";

        protected override void SubscribeToEvents()
        {
            Subscribe<InvoiceEvent>();
            base.SubscribeToEvents();
        }

        protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
        {
            if (evt is InvoiceEvent invoiceEvent)
            {
                var invoice = invoiceEvent.Invoice;
                var shopifyOrderId = invoice.Metadata?.OrderId;
                // We're only registering transaction on confirmed or complete and if invoice has orderId
                if ((invoice.Status == Client.Models.InvoiceStatus.Confirmed ||
                     invoice.Status == Client.Models.InvoiceStatus.Complete)
                    && shopifyOrderId != null)
                {
                    var storeData = await _storeRepository.FindStore(invoice.StoreId);
                    var storeBlob = storeData.GetStoreBlob();

                    // ensure that store in question has shopify integration turned on 
                    // and that invoice's orderId has shopify specific prefix
                    if (storeBlob.Shopify?.IntegratedAt.HasValue == true &&
                        shopifyOrderId.StartsWith(SHOPIFY_ORDER_ID_PREFIX, StringComparison.OrdinalIgnoreCase))
                    {
                        shopifyOrderId = shopifyOrderId[SHOPIFY_ORDER_ID_PREFIX.Length..];

                        var client = CreateShopifyApiClient(storeBlob.Shopify);
                        if (!await client.OrderExists(shopifyOrderId))
                        {
                            // don't register transactions for orders that don't exist on shopify
                            return;
                        }

                        // if we got this far, we likely need to register this invoice's payment on Shopify
                        // OrderTransactionRegisterLogic has check if transaction is already registered which is why we're passing invoice.Id
                        try
                        {
                            var logic = new OrderTransactionRegisterLogic(client);
                            var resp = await logic.Process(shopifyOrderId, invoice.Id, invoice.Currency,
                                invoice.Price.ToString(CultureInfo.InvariantCulture));
                            if (resp != null)
                            {
                                Logs.PayServer.LogInformation("Registered order transaction on Shopify. " +
                                                              $"Triggered by invoiceId: {invoice.Id}, Shopify orderId: {shopifyOrderId}");
                            }
                        }
                        catch (Exception ex)
                        {
                            Logs.PayServer.LogError(ex, $"Shopify error while trying to register order transaction. " +
                                                        $"Triggered by invoiceId: {invoice.Id}, Shopify orderId: {shopifyOrderId}");
                        }
                    }
                }
            }

            await base.ProcessEvent(evt, cancellationToken);
        }


        private ShopifyApiClient CreateShopifyApiClient(ShopifySettings shopify)
        {
            return new ShopifyApiClient(_httpClientFactory, shopify.CreateShopifyApiCredentials());
        }
    }
}
