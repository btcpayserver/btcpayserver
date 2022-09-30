#nullable enable
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Labels;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services
{
    public class TransactionTag
    {
        public string Label { get; }
        public string Id { get; }
        public JObject? AssociatedData { get; }

        public TransactionTag(string label, string? id = null, JObject? associatedData = null)
        {
            Label = label;
            Id = id ?? string.Empty;
            AssociatedData = associatedData;
        }
        public static TransactionTag PayjoinTag()
        {
            return new TransactionTag("payjoin");
        }
        public static TransactionTag InvoiceTag(string invoice)
        {
            return new TransactionTag("invoice", invoice);
        }
        public static TransactionTag PaymentRequestTag(string paymentRequestId)
        {
            return new TransactionTag("payment-request", paymentRequestId);
        }
        public static TransactionTag AppTag(string appId)
        {
            return new TransactionTag("app", appId);
        }

        public static TransactionTag PayjoinExposedTag(string? invoice)
        {
            return new TransactionTag("pj-exposed", invoice);
        }

        public static TransactionTag PayoutTag(string pullPaymentId, string payoutId)
        {
            return new TransactionTag("payout", payoutId, string.IsNullOrEmpty(pullPaymentId) ? null : new JObject()
            {
                ["pullPaymentId"] = pullPaymentId
            });
        }
    }
}
