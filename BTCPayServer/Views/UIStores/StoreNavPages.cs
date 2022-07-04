using System;

namespace BTCPayServer.Views.Stores
{
    public enum StoreNavPages
    {
        Create,
        Dashboard,
        General,
        Rates,
        OnchainSettings,
        LightningSettings,
        Lightning,
        CheckoutAppearance,
        Tokens,
        Users,
        PayButton,
        Plugins,
        Webhooks,
        PullPayments,
        Payouts,
        [Obsolete("Use StoreNavPages.Plugins instead")]
        Integrations,
        Emails
    }
}
