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
            // only register transactions if first, parent_id transaction is present and we haven't already registered transaction for this invoice( or if there was one registered but it was a failure and this one is success, in the case of a merchant marking it as complete)
            if (existingShopifyOrderTransactions != null && existingShopifyOrderTransactions.Count >= 1 && existingShopifyOrderTransactions.All(a => a.authorization != invoiceId || (!success || a.status == "failure")))
            {
                var transaction = existingShopifyOrderTransactions[0];

                if (currency.ToUpperInvariant().Trim() != transaction.currency.ToUpperInvariant().Trim())
                {
                    // because of parent_id present, currency will always be the one from parent transaction
                    // malicious attacker could potentially exploit this by creating invoice 
                    // in different currency and paying that one, registering order on Shopify as paid
                    // so if currency is supplied and is different from parent transaction currency we just won't register
                    return null;
                }

                var createTransaction = new TransactionsCreateReq
                {
                    transaction = new TransactionsCreateReq.DataHolder
                    {
                        parent_id = transaction.id,
                        currency = currency,
                        amount = amountCaptured,
                        kind = "capture",
                        gateway = "BTCPayServer",
                        source = "external",
                        authorization = invoiceId,
                        status = success? "success": "failure"
                    }
                };

                var createResp = await _client.TransactionCreate(orderId, createTransaction);
                return createResp;
            }

            return null;
        }
    }
}
