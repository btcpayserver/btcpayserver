using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
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
            dynamic resp = await _client.TransactionsList(orderId);

            JArray transactions = resp.transactions;
            if (transactions != null && transactions.Count >= 1)
            {
                dynamic transaction = transactions[0];

                if (currency != null && currency.Equals(transaction.currency, StringComparison.OrdinalIgnoreCase))
                {
                    // because of parent_id present, currency will always be the one from parent transaction
                    // malicious attacker could potentially exploit this by creating invoice 
                    // in different currency and paying that one, registering order on Shopify as paid
                    // so if currency is supplied and is different from parent transaction currency we just won't register
                    return null;
                }

                var createTransaction = new TransactionCreate
                {
                    transaction = new TransactionCreate.DataHolder
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
