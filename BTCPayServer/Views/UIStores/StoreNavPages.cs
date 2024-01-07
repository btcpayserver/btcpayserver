using System;

namespace BTCPayServer.Views.Stores
{
    public enum StoreNavPages
    {
        Index,
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
        [Obsolete("Use custom categories for your plugin/integration instead")]
        Plugins,
        Webhooks,
        PullPayments,
        Reporting,
        Payouts,
        PayoutProcessors,
        [Obsolete("Use StoreNavPages.Plugins instead")]
        Integrations,
        Emails,
        Forms,
        Roles
    }
}
