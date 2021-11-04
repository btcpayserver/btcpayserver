#if ALTCOINS
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Altcoins.Zcash.Payments
{
    public class ZcashSupportedPaymentMethod : ISupportedPaymentMethod
    {

        public string CryptoCode { get; set; }
        public long AccountIndex { get; set; }
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, ZcashPaymentType.Instance);
    }
}
#endif
