using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentData : CryptoPaymentData
    {
        [JsonIgnore]
        public BTCPayNetworkBase Network { get; set; }
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }
        public string BOLT11 { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 PaymentHash { get; set; }
        public string PaymentType { get; set; }

        public string GetDestination()
        {
            return BOLT11;
        }

        public decimal NetworkFee { get; set; }


        public string GetPaymentId()
        {
            // Legacy, some old payments don't have the PaymentHash set
            return PaymentHash?.ToString() ?? BOLT11;
        }

        public PaymentType GetPaymentType()
        {
            return string.IsNullOrEmpty(PaymentType) ? PaymentTypes.LightningLike : PaymentTypes.Parse(PaymentType);
        }

        public string[] GetSearchTerms()
        {
            return new[] { BOLT11 };
        }

        public decimal GetValue()
        {
            return Amount.ToDecimal(LightMoneyUnit.BTC);
        }

        public bool PaymentCompleted(PaymentEntity entity)
        {
            return true;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy)
        {
            return true;
        }
    }
}
