#nullable enable

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
        public PaymentMethodId GetPaymentMethodId(string cryptoCode) => new ($"{cryptoCode.ToUpperInvariant()}-{_paymentType}");
        public override string ToString()
        {
            return _paymentType;
        }
    }
}
