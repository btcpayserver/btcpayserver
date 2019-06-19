using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Bitcoin
{

    public class ManualPaymentData : CryptoPaymentData
    {  
        [JsonIgnore]
        public BTCPayNetworkBase Network { get; set; } = null;
        public string GetPaymentId()
        {
            return Timestamp.ToUnixTimestamp().ToString(CultureInfo.InvariantCulture);
        }

        public string[] GetSearchTerms()
        {
            return new string[0];
        }

        public decimal GetValue()
        {
            return GivenAmount - GivenBack;
        }

        public bool PaymentCompleted(PaymentEntity entity)
        {
            return true;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy)
        {
            return true;
        }

        public PaymentType GetPaymentType()
        {
            return PaymentTypes.Manual;
        }

        public string GetDestination()
        {
            return string.Empty;
        }

        public ManualPaymentData()
        {

        }

        public DateTime Timestamp { get; set; }

        public string GivenCurrencyCode { get; set; }
        public decimal GivenAmount { get; set; }
        public decimal GivenBack { get; set; }
        

    }
}
