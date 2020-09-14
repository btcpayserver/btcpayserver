#if ALTCOINS
using BTCPayServer.Contracts;

namespace BTCPayServer.Services.Altcoins.Stripe
{
    public class StripeStoreNavExtension : IStoreNavExtension
    {
        public string Partial { get; } = "Stripe/StoreNavStripeExtension";
    }
}
#endif
