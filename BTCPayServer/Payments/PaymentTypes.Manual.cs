using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Models;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class ManualPaymentType : PaymentType
    {
        public static ManualPaymentType Instance { get; } = new ManualPaymentType();
        private ManualPaymentType()
        {

        }

        public override string ToPrettyString() => "Manual";
        public override string GetId() => "Manual";

        public override CryptoPaymentData DeserializePaymentData(string str, params object[] additionalData)
        {
            return JsonConvert.DeserializeObject<ManualPaymentData>(str);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(string str)
        {
            return JsonConvert.DeserializeObject<ManualPaymentMethod>(str);
        }

        public override ISupportedPaymentMethod DeserializeSupportedPaymentMethod(BTCPayNetworkProvider networkProvider, PaymentMethodId paymentMethodId, JToken value)
        {
            if (value == null)
                throw new ArgumentNullException(nameof(value));
            return value.ToObject<ManualPaymentSettings>();
        }

        public override string GetTransactionLink(BTCPayNetworkBase network, string txId)
        {
            return "N/A";
        }

        public override bool IsAvailable(PaymentMethodId paymentMethodId, BTCPayNetworkProvider networkProvider)
        {
            return true;
        }

        public override IPaymentMethodHandler GetPaymentMethodHandler(PaymentMethodHandlerDictionary paymentMethodHandlerDictionary,
            PaymentMethodId paymentMethodId)
        {
            return paymentMethodHandlerDictionary[ManualPaymentSettings.StaticPaymentId];
        }

        public override string InvoiceViewPaymentPartialName { get; } = "ViewManualLikePaymentData";

        public override IEnumerable<CurrencyPair> GetCurrencyPairs(
            ISupportedPaymentMethod method, string targetCurrencyCode,
            StoreBlob storeBlob)
        {
            //we dont need to return anything as the manual method uses same currency as invoice
            return new List<CurrencyPair>()
            {
                new CurrencyPair(targetCurrencyCode, targetCurrencyCode)
            };
        }
    }
}
