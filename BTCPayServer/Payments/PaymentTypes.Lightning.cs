using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

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
    }
}
