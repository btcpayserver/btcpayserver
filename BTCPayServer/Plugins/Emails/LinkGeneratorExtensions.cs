#nullable  enable
using BTCPayServer.Abstractions;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.Controllers;
using Microsoft.AspNetCore.Routing;

namespace Microsoft.AspNetCore.Mvc;

public static class EmailsUrlHelperExtensions
{
    public class EmailRuleParams
    {
        public string? OfferingId { get; set; }
        public string? Trigger { get; set; }
        public string? Condition { get; set; }
        public string? RedirectUrl { get; set; }
        public string? To { get; set; }
    }

    public static string CreateEmailRuleLink(this LinkGenerator linkGenerator, string storeId, RequestBaseUrl baseUrl,
        EmailRuleParams? param = null)
        => linkGenerator.GetUriByAction(
            action: nameof(UIStoreEmailRulesController.StoreEmailRulesCreate),
            controller: "UIStoreEmailRules",
            values: new { area = EmailsPlugin.Area, storeId, offeringId = param?.OfferingId, trigger = param?.Trigger, condition = param?.Condition, redirectUrl = param?.RedirectUrl, to = param?.To },
            baseUrl);

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
