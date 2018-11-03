using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.JsonConverters;
using BTCPayServer.Lightning;
using BTCPayServer.Lightning.JsonConverters;
using BTCPayServer.Payments.Lightning;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning
{
    public class LightningLikePaymentData : CryptoPaymentData
    {
        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }
        public string BOLT11 { get; set; }
        public string GetPaymentId()
        {
            return BOLT11;
        }

        public PaymentTypes GetPaymentType()
        {
            return PaymentTypes.LightningLike;
        }

        public string[] GetSearchTerms()
        {
            return new[] { BOLT11 };
        }

        public decimal GetValue()
        {
            return Amount.ToDecimal(LightMoneyUnit.BTC);
        }

        public bool PaymentCompleted(PaymentEntity entity, BTCPayNetwork network)
        {
            return true;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy, BTCPayNetwork network)
        {
            return true;
        }
    }
}
