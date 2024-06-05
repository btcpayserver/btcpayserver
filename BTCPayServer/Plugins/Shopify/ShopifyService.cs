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
using BTCPayServer.Plugins.Shopify.ApiModels;
using BTCPayServer.Plugins.Shopify.Models;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Stores;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins.Shopify
{
    public class ShopifyService : EventHostedServiceBase
    {
        private readonly StoreRepository _storeRepository;
        private readonly InvoiceRepository _invoiceRepository;
        private readonly IHttpClientFactory _httpClientFactory;

        public ShopifyService(EventAggregator eventAggregator,
            StoreRepository storeRepository,
            InvoiceRepository invoiceRepository,
            IHttpClientFactory httpClientFactory,
            Logs logs) : base(eventAggregator, logs)
        {
            _storeRepository = storeRepository;
            _invoiceRepository = invoiceRepository;
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
            if (evt is InvoiceEvent invoiceEvent && new[]
                {
                    InvoiceEvent.MarkedCompleted, 
                    InvoiceEvent.MarkedInvalid, 
                    InvoiceEvent.Expired,
                    InvoiceEvent.Confirmed,
                    InvoiceEvent.Completed
                }.Contains(invoiceEvent.Name))
            {
                var invoice = invoiceEvent.Invoice;
                var shopifyOrderId = invoice.GetInternalTags(SHOPIFY_ORDER_ID_PREFIX).FirstOrDefault();
                if (shopifyOrderId != null)
                {
                    var success = invoice.Status switch
                    {
                        InvoiceStatus.Settled => true,
                        InvoiceStatus.Invalid or InvoiceStatus.Expired => false,
                        _ => (bool?)null
                    };

                    if (success.HasValue)
                        await RegisterTransaction(invoice, shopifyOrderId, success.Value);
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
                    var resp = await Process(client, shopifyOrderId, invoice.Id, invoice.Currency,
                        invoice.Price.ToString(CultureInfo.InvariantCulture), success);
                    if (resp != null)
                    {
                        await _invoiceRepository.AddInvoiceLogs(invoice.Id, resp);
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


        private static string[] _keywords = new[] {"bitcoin", "btc", "btcpayserver", "btcpay server"};

        public async Task<InvoiceLogs> Process(ShopifyApiClient client, string orderId, string invoiceId,
            string currency, string amountCaptured, bool success)
        {
            var result = new InvoiceLogs();
            currency = currency.ToUpperInvariant().Trim();
            var existingShopifyOrderTransactions = (await client.TransactionsList(orderId)).transactions;

            //if there isn't a record for btcpay payment gateway, abort
            var baseParentTransaction = existingShopifyOrderTransactions.FirstOrDefault(holder =>
                _keywords.Any(a => holder.gateway.Contains(a, StringComparison.InvariantCultureIgnoreCase)));
            if (baseParentTransaction is null)
            {
                result.Write("Couldn't find the order on Shopify.", InvoiceEventData.EventSeverity.Error);
                return result;
            }

            //technically, this exploit should not be possible as we use internal invoice tags to verify that the invoice was created by our controlled, dedicated endpoint.
            if (currency.ToUpperInvariant().Trim() != baseParentTransaction.currency.ToUpperInvariant().Trim())
            {
                // because of parent_id present, currency will always be the one from parent transaction
                // malicious attacker could potentially exploit this by creating invoice 
                // in different currency and paying that one, registering order on Shopify as paid
                // so if currency is supplied and is different from parent transaction currency we just won't register
                result.Write("Currency mismatch on Shopify.", InvoiceEventData.EventSeverity.Error);
                return result;
            }

            var kind = "capture";
            var parentId = baseParentTransaction.id;
            var status = success ? "success" : "failure";
            //find all existing transactions recorded around this invoice id 
            var existingShopifyOrderTransactionsOnSameInvoice =
                existingShopifyOrderTransactions.Where(holder => holder.authorization == invoiceId);

            //filter out the successful ones
            var successfulActions =
                existingShopifyOrderTransactionsOnSameInvoice.Where(holder => holder.status == "success").ToArray();

            //of the successful ones, get the ones we registered as a valid payment
            var successfulCaptures = successfulActions.Where(holder => holder.kind == "capture").ToArray();

            //of the successful ones, get the ones we registered as a voiding of a previous successful payment
            var refunds = successfulActions.Where(holder => holder.kind == "refund").ToArray();

            //if we are working with a non-success registration, but see that we have previously registered this invoice as a success, we switch to creating a "void" transaction, which in shopify terms is a refund.
            if (!success && successfulCaptures.Length > 0 && (successfulCaptures.Length - refunds.Length) > 0)
            {
                kind = "void";
                parentId = successfulCaptures.Last().id;
                status = "success";
                result.Write(
                    "A transaction was previously recorded against the Shopify order. Creating a void transaction.",
                    InvoiceEventData.EventSeverity.Warning);
            }
            else if (!success)
            {
                kind = "void";
                status = "success";
                result.Write("Attempting to void the payment on Shopify order due to failure in payment.",
                    InvoiceEventData.EventSeverity.Warning);
            }
            //if we are working with a success registration, but can see that we have already had a successful transaction saved, get outta here
            else if (success && successfulCaptures.Length > 0 && (successfulCaptures.Length - refunds.Length) > 0)
            {
                result.Write("A transaction was previously recorded against the Shopify order. Skipping.",
                    InvoiceEventData.EventSeverity.Warning);
                return result;
            }

            var createTransaction = new TransactionsCreateReq
            {
                transaction = new TransactionsCreateReq.DataHolder
                {
                    parent_id = parentId,
                    currency = currency,
                    amount = amountCaptured,
                    kind = kind,
                    gateway = "BTCPayServer",
                    source = "external",
                    authorization = invoiceId,
                    status = status
                }
            };
            var createResp = await client.TransactionCreate(orderId, createTransaction);

            if (createResp.transaction is null)
            {
                result.Write("Failed to register the transaction on Shopify.", InvoiceEventData.EventSeverity.Error);
            }
            else
            {
                result.Write(
                    $"Successfully registered the transaction on Shopify. tx status:{createResp.transaction.status}, kind: {createResp.transaction.kind}, order id:{createResp.transaction.order_id}",
                    InvoiceEventData.EventSeverity.Info);
            }

            if (!success)
            {
                try
                {
                    await client.CancelOrder(orderId);
                    result.Write("Cancelling the Shopify order.", InvoiceEventData.EventSeverity.Warning);
                }
                catch (Exception e)
                {
                    result.Write($"Failed to cancel the Shopify order. {e.Message}",
                        InvoiceEventData.EventSeverity.Error);
                }
            }

            return result;
        }
    }
}
