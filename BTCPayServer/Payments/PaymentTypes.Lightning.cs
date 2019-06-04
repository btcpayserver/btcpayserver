using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Services.Invoices;
using Newtonsoft.Json;

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

        public override CryptoPaymentData DeserializePaymentData(string str)
        {
            return JsonConvert.DeserializeObject<Payments.Lightning.LightningLikePaymentData>(str);
        }

        public override IPaymentMethodDetails DeserializePaymentMethodDetails(string str)
        {
            return JsonConvert.DeserializeObject<Payments.Lightning.LightningLikePaymentMethodDetails>(str);
        }
    }
}
