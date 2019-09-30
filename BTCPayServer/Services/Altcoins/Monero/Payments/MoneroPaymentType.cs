using System.Collections.Generic;
using System.Globalization;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Altcoins.Monero.Payments
{
    public class MoneroPaymentType: PaymentType
    {
        public static MoneroPaymentType Instance { get; } = new MoneroPaymentType();
        public override string ToPrettyString() => "On-Chain";

        public override string GetId()=> "MoneroLike";

        public override CryptoPaymentData DeserializePaymentData(string str, BTCPayNetworkBase network)
        {
#pragma warning disable CS0618
            return JsonConvert.DeserializeObject<MoneroLikePaymentData>(str);
        }

        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return JsonConvert.SerializeObject(paymentData);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(string str)
        {
            return JsonConvert.DeserializeObject<MoneroLikeOnChainPaymentMethodDetails>(str);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkProvider networkProvider,
            PaymentMethodId paymentMethodId, JToken value)
        {
            return JsonConvert.DeserializeObject<MoneroSupportedPaymentMethod>(value.ToString());
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network, JToken value)
        {
            return JsonConvert.DeserializeObject<MoneroSupportedPaymentMethod>(value.ToString());
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return string.Format(CultureInfo.InvariantCulture, network.BlockExplorerLink, txId);
        }

        public override string InvoiceViewPaymentPartialName { get; }= "Monero/ViewMoneroLikePaymentData";
        public override IEnumerable<CurrencyPair> GetCurrencyPairs(ISupportedPaymentMethod supportedPaymentMethod, string targetCurrencyCode, StoreBlob blob)
        {
            return new[] {new CurrencyPair(supportedPaymentMethod.PaymentId.CryptoCode, targetCurrencyCode)};
        }
    }
}
