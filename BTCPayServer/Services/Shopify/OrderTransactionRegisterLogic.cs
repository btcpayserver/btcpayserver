using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Shopify.ApiModels;
using Microsoft.EntityFrameworkCore.Internal;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Shopify
{
    public class OrderTransactionRegisterLogic
    {
        private readonly ShopifyApiClient _client;

        public OrderTransactionRegisterLogic(ShopifyApiClient client)
        {
            _client = client;
        }

        public async Task<TransactionsCreateResp> Process(string orderId, string invoiceId, string currency, string amountCaptured, bool success)
        {
            currency = currency.ToUpperInvariant().Trim();
            var existingShopifyOrderTransactions = (await _client.TransactionsList(orderId)).transactions;

            if (existingShopifyOrderTransactions?.Count < 1)
            {
                return null;
            }
            //TODO: verify if we should be doing this or filtering out the parent transaction by the gateway (the one that shows in the checkout UI, aka the manual payment method created by the merchant)
            var baseParentTransaction = existingShopifyOrderTransactions[0];
            
            //technically, this exploit should not be possible as we use internal invoice tags to verify that the invoice was created by our controlled, dedicated endpoint.
            if (currency.ToUpperInvariant().Trim() != baseParentTransaction.currency.ToUpperInvariant().Trim())
            {
                // because of parent_id present, currency will always be the one from parent transaction
                // malicious attacker could potentially exploit this by creating invoice 
                // in different currency and paying that one, registering order on Shopify as paid
                // so if currency is supplied and is different from parent transaction currency we just won't register
                return null;
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
            }
            //if we are working with a success registration, but can see that we have already had a successful transaction saved, get outta here
            else if(success && successfulCaptures.Length >0 && (successfulCaptures.Length - refunds.Length  ) > 0 ) 
            {
                return null;
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
            var createResp = await _client.TransactionCreate(orderId, createTransaction);
            return createResp;
        }
    }
}
