using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Payments
{
    public class BitcoinPaymentType : PaymentType
    {
        public static BitcoinPaymentType Instance { get; } = new BitcoinPaymentType();
        private BitcoinPaymentType()
        {

        }

        public override string ToPrettyString() => "On-Chain";
        public override string GetId() => "BTCLike";
    }
}
