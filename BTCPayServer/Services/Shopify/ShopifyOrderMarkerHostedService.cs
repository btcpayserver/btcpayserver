using System;
using System.Globalization;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using NBXplorer;

namespace BTCPayServer.Services.Shopify
{
    public class ShopifyOrderMarkerHostedService : IHostedService
    {
        private readonly EventAggregator _eventAggregator;
        private readonly StoreRepository _storeRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        public ShopifyOrderMarkerHostedService(EventAggregator eventAggregator, StoreRepository storeRepository, IHttpClientFactory httpClientFactory)
        {
            _eventAggregator = eventAggregator;
            _storeRepository = storeRepository;
            _httpClientFactory = httpClientFactory;
        }

        private CancellationTokenSource _Cts;
        private readonly CompositeDisposable leases = new CompositeDisposable();

        public Task StartAsync(CancellationToken cancellationToken)
        {
            _Cts = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

            leases.Add(_eventAggregator.Subscribe<Events.InvoiceEvent>(async b =>
            {
                var invoice = b.Invoice;
                var shopifyOrderId = invoice.Metadata?.OrderId;
                if (invoice.Status == Client.Models.InvoiceStatus.Paid && shopifyOrderId != null)
                {
                    var storeData = await _storeRepository.FindStore(invoice.StoreId);
                    var storeBlob = storeData.GetStoreBlob();

                    if (storeBlob.Shopify?.IntegratedAt.HasValue == true)
                    {
                        var client = createShopifyApiClient(storeBlob.Shopify);
                        if (!await client.OrderExists(shopifyOrderId))
                        {
                            // don't register transactions for orders that don't exist on shopify
                            return;
                        }

                        try
                        {
                            var logic = new OrderTransactionRegisterLogic(client);
                            await logic.Process(shopifyOrderId, invoice.Currency, invoice.Price.ToString(CultureInfo.InvariantCulture));
                            Logs.PayServer.LogInformation("Registered order transaction on Shopify. " +
                                $"Triggered by invoiceId: {invoice.Id}, Shopify orderId: {shopifyOrderId}");
                        }
                        catch (Exception ex)
                        {
                            Logs.PayServer.LogError(ex, $"Shopify error while trying to register order transaction. " +
                                $"Triggered by invoiceId: {invoice.Id}, Shopify orderId: {shopifyOrderId}");
                        }
                    }
                }
            }));
            return Task.CompletedTask;
        }

        private ShopifyApiClient createShopifyApiClient(StoreBlob.ShopifyDataHolder shopify)
        {
            return new ShopifyApiClient(_httpClientFactory, null, new ShopifyApiClientCredentials
            {
                ShopName = shopify.ShopName,
                ApiKey = shopify.ApiKey,
                ApiPassword = shopify.Password,
                SharedSecret = shopify.SharedSecret
            });
        }

        public Task StopAsync(CancellationToken cancellationToken)
        {
            _Cts?.Cancel();
            leases.Dispose();

            return Task.CompletedTask;
        }
    }
}
