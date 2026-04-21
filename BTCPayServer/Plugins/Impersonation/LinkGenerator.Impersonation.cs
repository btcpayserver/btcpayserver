#nullable enable
using BTCPayServer.Abstractions;
using BTCPayServer.Plugins.Impersonation;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Mvc;

public static class ImpersonationUrlHelperExtensions
{
    public static string LoginCodeLink(this LinkGenerator urlHelper, string loginCode, string? returnUrl, bool? impersonate, RequestBaseUrl requestBaseUrl)
    {
        return urlHelper.GetUriByAction(nameof(UIImpersonationController.LoginUsingCode), "UIImpersonation", new { area = ImpersonationPlugin.Area, loginCode, returnUrl, impersonate }, requestBaseUrl);
    }
}
