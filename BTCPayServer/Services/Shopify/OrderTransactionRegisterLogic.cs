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

        public async Task<dynamic> Process(string orderId, string currency = null, string amountCaptured = null)
        {
            var resp = await _client.TransactionsList(orderId);

            var txns = resp.transactions;
            if (txns != null && txns.Count >= 1)
            {
                var transaction = txns[0];

                if (currency != null && currency.ToUpperInvariant().Trim() != transaction.currency.ToString().ToUpperInvariant().Trim())
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
                        currency = transaction.currency,
                        amount = amountCaptured ?? transaction.amount,
                        kind = "capture",
                        gateway = "BTCPayServer",
                        source = "external"
                    }
                };

                dynamic createResp = await _client.TransactionCreate(orderId, createTransaction);
                return createResp;
            }

            return null;
        }
    }
}
