using System;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class LightningPaymentType : PaymentType
    {
        public static LightningPaymentType Instance { get; } = new LightningPaymentType();

        private LightningPaymentType() { }

        public override string ToPrettyString() => "Off-Chain";
        public override string GetId() => "LightningLike";
        public override string GetBadge() => "⚡";
        public override string ToStringNormalized() => "LightningNetwork";

        public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
        {
            return ((BTCPayNetwork)network)?.ToObject<LightningLikePaymentData>(str);
        }

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return ((BTCPayNetwork)network).ToString(paymentData);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<LightningLikePaymentMethodDetails>(str);
        }

        public override string SerializePaymentMethodDetails(BTCPayNetworkBase network, IPaymentMethodDetails details)
        {
            return JsonConvert.SerializeObject(details);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network,
            JToken value)
        {
            return JsonConvert.DeserializeObject<LightningSupportedPaymentMethod>(value.ToString());
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return null;
        }

        public override string GetPaymentLink(BTCPayNetworkBase network, IPaymentMethodDetails paymentMethodDetails,
            Money cryptoInfoDue, string serverUri)
        {
            if (!paymentMethodDetails.Activated)
            {
                return string.Empty;
            }
            var lnInvoiceTrimmedOfScheme = paymentMethodDetails.GetPaymentDestination().ToLowerInvariant()
                .Replace("lightning:", "", StringComparison.InvariantCultureIgnoreCase);

            return $"lightning:{lnInvoiceTrimmedOfScheme}";
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Lightning/ViewLightningLikePaymentData";
        public override bool IsPaymentType(string paymentType)
        {
            return paymentType?.Equals("offchain", StringComparison.InvariantCultureIgnoreCase) is true || base.IsPaymentType(paymentType);
        }
    }
}
