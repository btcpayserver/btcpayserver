#nullable enable
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
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
            return new Attachment(WalletObjectData.Types.Payjoin);
        }
        public static Attachment Invoice(string invoice)
        {
            return new Attachment(WalletObjectData.Types.Invoice, invoice);
        }
        public static Attachment PaymentRequest(string paymentRequestId)
        {
            return new Attachment(WalletObjectData.Types.PaymentRequest, paymentRequestId);
        }
        public static Attachment App(string appId)
        {
            return new Attachment(WalletObjectData.Types.App, appId);
        }

        public static Attachment PayjoinExposed(string? invoice)
        {
            return new Attachment(WalletObjectData.Types.PayjoinExposed, invoice);
        }

        public static IEnumerable<Attachment> Payout(string? pullPaymentId, string payoutId)
        {
            if (string.IsNullOrEmpty(pullPaymentId))
            {
                yield return new Attachment(WalletObjectData.Types.Payout, payoutId);
            }
            else
            {
                yield return new Attachment(WalletObjectData.Types.Payout, payoutId, new JObject()
                {
                    ["pullPaymentId"] = pullPaymentId
                });
                yield return new Attachment(WalletObjectData.Types.PullPayment, pullPaymentId);
            }
        }
    }
}
