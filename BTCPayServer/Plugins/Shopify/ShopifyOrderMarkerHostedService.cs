using System;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Plugins.Shopify.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Shopify
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
            if (evt is InvoiceEvent invoiceEvent && !new[]
            {
                InvoiceEvent.Created, InvoiceEvent.ExpiredPaidPartial,
                InvoiceEvent.ReceivedPayment, InvoiceEvent.PaidInFull
            }.Contains(invoiceEvent.Name))
            {
                var invoice = invoiceEvent.Invoice;
                var shopifyOrderId = invoice.GetInternalTags(SHOPIFY_ORDER_ID_PREFIX).FirstOrDefault();
                if (shopifyOrderId != null)
                {
                    if (new[] {InvoiceStatusLegacy.Invalid, InvoiceStatusLegacy.Expired}.Contains(invoice.GetInvoiceState()
                        .Status) && invoice.ExceptionStatus != InvoiceExceptionStatus.None)
                    {
                        //you have failed us, customer

                        await RegisterTransaction(invoice, shopifyOrderId, false);
                    }
                    else if (new[] {InvoiceStatusLegacy.Complete, InvoiceStatusLegacy.Confirmed}.Contains(
                        invoice.Status))
                    {
                        await RegisterTransaction(invoice, shopifyOrderId, true);
                    }
                }
            }

            await base.ProcessEvent(evt, cancellationToken);
        }

        private async Task RegisterTransaction(InvoiceEntity invoice, string shopifyOrderId, bool success)
        {
            var storeData = await _storeRepository.FindStore(invoice.StoreId);
            var storeBlob = storeData.GetStoreBlob();

            // ensure that store in question has shopify integration turned on 
            // and that invoice's orderId has shopify specific prefix
            var settings = storeBlob.GetShopifySettings();
            if (settings?.IntegratedAt.HasValue == true)
            {
                var client = CreateShopifyApiClient(settings);
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
                        invoice.Price.ToString(CultureInfo.InvariantCulture), success);
                    if (resp != null)
                    {
                        Logs.PayServer.LogInformation($"Registered order transaction {invoice.Price}{invoice.Currency} on Shopify. " +
                                                      $"Triggered by invoiceId: {invoice.Id}, Shopify orderId: {shopifyOrderId}, Success: {success}");
                    }
                }
                catch (Exception ex)
                {
                    Logs.PayServer.LogError(ex,
                        $"Shopify error while trying to register order transaction. " +
                        $"Triggered by invoiceId: {invoice.Id}, Shopify orderId: {shopifyOrderId}");
                }
            }
        }


        private ShopifyApiClient CreateShopifyApiClient(ShopifySettings shopify)
        {
            return new ShopifyApiClient(_httpClientFactory, shopify.CreateShopifyApiCredentials());
        }
    }
}
