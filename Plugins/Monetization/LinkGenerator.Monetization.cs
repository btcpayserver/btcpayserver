#nullable  enable
using BTCPayServer.Abstractions;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.Controllers;
using BTCPayServer.Plugins.Monetization;
using BTCPayServer.Plugins.Monetization.Controllers;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Mvc;

public static class MonetizationUrlHelperExtensions
{
    public static string UserManageBillingLink(this LinkGenerator linkGenerator, RequestBaseUrl baseUrl, bool fastRedirect = false)
        => linkGenerator.GetUriByAction(
            nameof(UIUserMonetizationController.ManageBilling),
            "UIUserMonetization",
            new { area = MonetizationPlugin.Area, fastRedirect },
            baseUrl);
}
