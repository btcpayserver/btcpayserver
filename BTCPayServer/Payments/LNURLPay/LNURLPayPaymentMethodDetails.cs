using System.Collections.Generic;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public class LNURLPayPaymentMethodDetails : LightningLikePaymentMethodDetails
    {
        public LightningSupportedPaymentMethod LightningSupportedPaymentMethod { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney GeneratedBoltAmount { get; set; }

        public string BTCPayInvoiceId { get; set; }
        public bool Bech32Mode { get; set; }

        public string ProvidedComment { get; set; }
        public string ConsumedLightningAddress { get; set; }

        public override PaymentType GetPaymentType()
        {
            return LNURLPayPaymentType.Instance;
        }

        public override string GetAdditionalDataPartialName()
        {
            if (string.IsNullOrEmpty(ProvidedComment) && string.IsNullOrEmpty(ConsumedLightningAddress))
            {
                return null;
            }

            return "LNURL/AdditionalPaymentMethodDetails";
        }

        public override Dictionary<string, JObject> GetAdditionalData()
        {
            var result = base.GetAdditionalData();
            if (!string.IsNullOrEmpty(ProvidedComment))
            {
                result.TryAdd(nameof(ProvidedComment), new JObject(ProvidedComment));
            }

            if (!string.IsNullOrEmpty(ConsumedLightningAddress))
            {
                result.TryAdd(nameof(ConsumedLightningAddress), new JObject(ConsumedLightningAddress));
            }

            return result;
        }
    }
}
