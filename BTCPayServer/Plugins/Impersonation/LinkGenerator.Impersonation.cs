#nullable enable
using BTCPayServer.Abstractions;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.Impersonation;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Mvc;

public static class ImpersonationUrlHelperExtensions
{
    public static string LoginCodeLink(this LinkGenerator urlHelper, string loginCode, string? returnUrl, RequestBaseUrl requestBaseUrl)
    {
        return urlHelper.GetUriByAction(nameof(UIAccountController.Login), "UIAccount", new { LoginCode = loginCode, returnUrl }, requestBaseUrl);
    }
}
