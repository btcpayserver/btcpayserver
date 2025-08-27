#nullable  enable
using BTCPayServer.Abstractions;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.Subscriptions.Controllers;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Plugins.Subscriptions;

public static class UrlHelperExtensions
{
    public static string SubscriberPortalLink(this LinkGenerator urlHelper, string portalSessionId, RequestBaseUrl requestBaseUrl, string? checkoutPlanId = null)
        => urlHelper.GetUriByAction(
            action: nameof(UISubscriberPortalController.SubscriberPortal),
            values: new { area = SubscriptionsPlugin.Area, portalSessionId, checkoutPlanId },
            controller: "UISubscriberPortal",
            requestBaseUrl: requestBaseUrl);

    public static string OfferingLink(this LinkGenerator urlHelper, string storeId, string offeringId, RequestBaseUrl requestBaseUrl)
   =>  urlHelper.GetUriByAction(
       action: nameof(UISubscriptionsController.Offering),
       values: new { area = SubscriptionsPlugin.Area, storeId, offeringId },
       controller: "UISubscriptions",
       requestBaseUrl: requestBaseUrl);

    public static string PlanCheckoutDefaultLink(this LinkGenerator urlHelper, RequestBaseUrl requestBaseUrl)
        => urlHelper.GetUriByAction(
            action: nameof(UIPlanCheckoutController.PlanCheckoutDefaultRedirect),
            values: new { area = SubscriptionsPlugin.Area },
            controller: "UIPlanCheckout",
            requestBaseUrl: requestBaseUrl);
}
