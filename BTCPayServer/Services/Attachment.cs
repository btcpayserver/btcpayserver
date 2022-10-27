#nullable enable
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Labels;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services
{
    public class Attachment
    {
        public string Type { get; }
        public string Id { get; }
        public JObject? Data { get; }

        public Attachment(string type, string? id = null, JObject? data = null)
        {
            Type = type;
            Id = id ?? string.Empty;
            Data = data;
        }
        public static Attachment Payjoin()
        {
            return new Attachment("payjoin");
        }
        public static Attachment Invoice(string invoice)
        {
            return new Attachment("invoice", invoice);
        }
        public static Attachment PaymentRequest(string paymentRequestId)
        {
            return new Attachment("payment-request", paymentRequestId);
        }
        public static Attachment App(string appId)
        {
            return new Attachment("app", appId);
        }

        public static Attachment PayjoinExposed(string? invoice)
        {
            return new Attachment("pj-exposed", invoice);
        }

        public static IEnumerable<Attachment> Payout(string? pullPaymentId, string payoutId)
        {
            if (string.IsNullOrEmpty(pullPaymentId))
            {
                yield return new Attachment("payout", payoutId);
            }
            else
            {
                yield return new Attachment("payout", payoutId, new JObject()
                {
                    ["pullPaymentId"] = pullPaymentId
                });
                yield return new Attachment("pull-payment", pullPaymentId);
            }
        }
    }
}
