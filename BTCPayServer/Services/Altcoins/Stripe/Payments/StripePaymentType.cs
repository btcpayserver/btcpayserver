#if ALTCOINS
using System.Globalization;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Stripe.Payments
{
    public class StripePaymentType : PaymentType
    {
        public static StripePaymentType Instance { get; } = new StripePaymentType();
        public override string ToPrettyString() => "Stripe";

        public override string GetId() => "Stripe";
        public override string ToStringNormalized()
        {
            return "Stripe";
        }

        public override CryptoPaymentData DeserializePaymentData(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<StripePaymentData>(str);
        }

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return JsonConvert.SerializeObject(paymentData);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(BTCPayNetworkBase network, string str)
        {
            return JsonConvert.DeserializeObject<StripePaymentMethodDetails>(str);
        }

        public override string SerializePaymentMethodDetails(BTCPayNetworkBase network, IPaymentMethodDetails details)
        {
            return JsonConvert.SerializeObject(details);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value)
        {
            return JsonConvert.DeserializeObject<StripeSupportedPaymentMethod>(value.ToString());
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return null;
        }

        public override string GetPaymentLink(BTCPayNetworkBase network, IPaymentMethodDetails paymentMethodDetails, Money cryptoInfoDue, string serverUri)
        {
            return "";
        }

        public override string InvoiceViewPaymentPartialName { get; } = "Stripe/ViewStripePaymentData";
    }
}
#endif
