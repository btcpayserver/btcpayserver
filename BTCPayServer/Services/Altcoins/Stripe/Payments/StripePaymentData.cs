#if ALTCOINS
using BTCPayServer.Client.Models;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;

namespace BTCPayServer.Services.Altcoins.Stripe.Payments
{
    public class StripePaymentData : CryptoPaymentData
    {
        public BTCPayNetworkBase Network { get; set; }

        public string GetPaymentId()
        {
            return SessionId ?? PaymentIntentId;
        }

        public string[] GetSearchTerms()
        {
            return new[] {GetPaymentId()};
        }

        public decimal GetValue()
        {
            return MoneyExtensions.Convert(Amount, Network.Divisibility);
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
            return StripePaymentType.Instance;
        }

        public string GetDestination()
        {
            return GetPaymentId();
        }

        public string SessionId { get; set; }
        public string PaymentIntentId { get; set; }
        public long Amount { get; set; }
        public string CryptoCode { get; set; }
    }
}
#endif
