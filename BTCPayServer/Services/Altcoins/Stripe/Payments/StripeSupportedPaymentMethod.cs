#if ALTCOINS
using BTCPayServer.Payments;

namespace BTCPayServer.Services.Altcoins.Stripe.Payments
{
    public class StripeSupportedPaymentMethod : ISupportedPaymentMethod
    {
        public string CryptoCode { get; set; }
        public string PublishableKey { get; set; }
        public string SecretKey { get; set; }
        public PaymentMethodId PaymentId => new PaymentMethodId(CryptoCode, StripePaymentType.Instance);
        public bool UseCheckout { get; set; }
    }
}
#endif
