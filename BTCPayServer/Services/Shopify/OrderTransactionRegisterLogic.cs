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
            
            
            var baseParentTransaction = existingShopifyOrderTransactions[0];
            
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
            var existingShopifyOrderTransactionsOnSameInvoice =
                existingShopifyOrderTransactions.Where(holder => holder.authorization == invoiceId);
            
            var successfulActions =
                existingShopifyOrderTransactionsOnSameInvoice.Where(holder => holder.status == "success").ToArray();

            var successfulCaptures = successfulActions.Where(holder => holder.kind == "capture").ToArray();
            var refunds = successfulActions.Where(holder => holder.kind == "refund").ToArray();
            
            if (!success && successfulCaptures.Length > 0 && (successfulCaptures.Length - refunds.Length) > 0)
            {
                kind = "void";
                parentId = successfulCaptures.Last().id;
                status = "success";
            }
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
