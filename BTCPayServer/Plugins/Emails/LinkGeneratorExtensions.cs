#nullable  enable
using BTCPayServer.Abstractions;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.Controllers;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Mvc;

public static class EmailsUrlHelperExtensions
{
    public static string GetStoreEmailRulesLink(this LinkGenerator linkGenerator, string storeId, RequestBaseUrl baseUrl)
    => linkGenerator.GetUriByAction(
        action: nameof(UIStoreEmailRulesController.StoreEmailRulesList),
        controller: "UIStoreEmailRules",
        values: new { area = EmailsPlugin.Area, storeId },
        baseUrl);
    public static string GetStoreEmailSettingsLink(this LinkGenerator linkGenerator, string storeId, RequestBaseUrl baseUrl)
        => linkGenerator.GetUriByAction(
            action: nameof(UIStoresEmailController.StoreEmailSettings),
            controller: "UIStoresEmail",
            values: new { area = EmailsPlugin.Area, storeId },
            baseUrl);
}
