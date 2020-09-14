using System;

#if ALTCOINS
namespace BTCPayServer.Services.Altcoins.Stripe.UI
{
    public class StripePaymentViewModel
    {
        public string Crypto { get; set; }
        public string Amount { get; set; }
        public string TransactionId { get; set; }
        public DateTimeOffset ReceivedTime { get; set; }
        public object TransactionLink { get; set; }
    }
}
#endif
