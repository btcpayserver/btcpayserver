#if ALTCOINS
using BTCPayServer.Payments;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Stripe.Payments
{
    public class StripePaymentMethodDetails : IPaymentMethodDetails
    {
        public string PublishableKey { get; set; }
        public string PaymentIntentClientSecret { get; set; }
        public string PaymentIntentId { get; set; }
        public string SessionId { get; set; }
        public bool Disabled { get; set; }
        public long Amount { get; set; }

        public string GetPaymentDestination()
        {
            return JsonConvert.SerializeObject(this);
        }

        public PaymentType GetPaymentType()
        {
            return StripePaymentType.Instance;
        }

        public decimal GetNextNetworkFee()
        {
            return 0m;
        }

        public decimal GetFeeRate()
        {
            return 0m;
        }

        public void SetPaymentDestination(string newPaymentDestination)
        {
            var mapped = JsonConvert.DeserializeObject<StripePaymentMethodDetails>(newPaymentDestination);
            PublishableKey = mapped.PublishableKey;
            PaymentIntentClientSecret = mapped.PaymentIntentClientSecret;
            PaymentIntentId = mapped.PaymentIntentId;
            SessionId = mapped.SessionId;
            Disabled = mapped.Disabled;
            Amount = mapped.Amount;
        }
    }
}
#endif
