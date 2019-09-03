using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class LightningPaymentType : PaymentType
    {
        public static LightningPaymentType Instance { get; } = new LightningPaymentType();
        private LightningPaymentType()
        {

        }

        public override string ToPrettyString() => "Off-Chain";
        public override string GetId() => "LightningLike";

        public override CryptoPaymentData DeserializePaymentData(string str, BTCPayNetworkBase network)
        {
            var result = JsonConvert.DeserializeObject<LightningLikePaymentData>(str);
            result.Network = network;
            return result;
        }
        public override string SerializePaymentData(BTCPayNetworkBase network, CryptoPaymentData paymentData)
        {
            return ((BTCPayNetwork) network).ToString(paymentData);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(string str)
        {
            return JsonConvert.DeserializeObject<Payments.Lightning.LightningLikePaymentMethodDetails>(str);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkBase network,
            JToken value)
        {
            return JsonConvert.DeserializeObject<LightningSupportedPaymentMethod>(value.ToString());
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkProvider networkProvider, PaymentMethodId paymentMethodId, JToken value)
        {
            return JsonConvert.DeserializeObject<LightningSupportedPaymentMethod>(value.ToString());
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return null;
        }
        public override string InvoiceViewPaymentPartialName { get; } = "ViewLightningLikePaymentData";

        public override IEnumerable<CurrencyPair> GetCurrencyPairs(ISupportedPaymentMethod supportedPaymentMethod,
            string targetCurrencyCode, StoreBlob storeBlob)
        {
            var result = new List<CurrencyPair> {new CurrencyPair(supportedPaymentMethod.PaymentId.CryptoCode, targetCurrencyCode)};

            if (storeBlob.LightningMaxValue != null)
                result.Add(new CurrencyPair(supportedPaymentMethod.PaymentId.CryptoCode, storeBlob.LightningMaxValue.Currency));
            return result;
        }
    }
}
