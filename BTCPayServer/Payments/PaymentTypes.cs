#nullable enable
using System;
using System.Linq;
#if ALTCOINS
using BTCPayServer.Services.Altcoins.Monero.Payments;
using BTCPayServer.Services.Altcoins.Zcash.Payments;
#endif
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Payments
{
    public static class PaymentTypes
    {
        public static readonly PaymentType CHAIN = new("CHAIN");
        public static readonly PaymentType LN = new("LN");
        public static readonly PaymentType LNURL = new("LNURL");
    }
    public class PaymentType
    {
        private readonly string _paymentType;
        public PaymentType(string paymentType)
        {
            _paymentType = paymentType;
        }
        public PaymentMethodId GetPaymentMethodId(string cryptoCode) => new (cryptoCode, _paymentType);
    }
}
