using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using BTCPayServer.Payments.Lightning;
using Newtonsoft.Json;

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
    }
}
