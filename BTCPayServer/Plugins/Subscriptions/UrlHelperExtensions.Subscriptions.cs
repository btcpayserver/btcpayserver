#nullable enable
using BTCPayServer.Abstractions;
using BTCPayServer.Plugins.Subscriptions.Controllers;
using BTCPayServer.Views.UIStoreMembership;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Plugins.Subscriptions;

public static class UrlHelperExtensions
{
    public static string PlanCheckout(this LinkGenerator urlHelper, string checkoutId, RequestBaseUrl requestBaseUrl)
        => urlHelper.GetUriByAction(
            action: nameof(UIPlanCheckoutController.PlanCheckout),
            values: new { area = SubscriptionsPlugin.Area, checkoutId },
            controller: "UIPlanCheckout",
            requestBaseUrl: requestBaseUrl);

    public static string SubscriberPortalLink(this LinkGenerator urlHelper, string portalSessionId, RequestBaseUrl requestBaseUrl, string? checkoutPlanId = null)
        => urlHelper.GetUriByAction(
            action: nameof(UISubscriberPortalController.SubscriberPortal),
            values: new { area = SubscriptionsPlugin.Area, portalSessionId, checkoutPlanId },
            controller: "UISubscriberPortal",
            requestBaseUrl: requestBaseUrl);

    public static string OfferingLink(this LinkGenerator urlHelper, string storeId, string offeringId, SubscriptionSection section, RequestBaseUrl requestBaseUrl)
   =>  urlHelper.GetUriByAction(
       action: nameof(UIOfferingController.Offering),
       values: new { area = SubscriptionsPlugin.Area, storeId, offeringId, section },
       controller: "UIOffering",
       requestBaseUrl: requestBaseUrl);

    public static string PlanCheckoutDefaultLink(this LinkGenerator urlHelper, RequestBaseUrl requestBaseUrl)
        => urlHelper.GetUriByAction(
            action: nameof(UIPlanCheckoutController.PlanCheckoutDefaultRedirect),
            values: new { area = SubscriptionsPlugin.Area },
            controller: "UIPlanCheckout",
            requestBaseUrl: requestBaseUrl);
}
