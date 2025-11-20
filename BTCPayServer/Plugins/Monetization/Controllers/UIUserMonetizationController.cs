using System.Threading.Tasks;
using BTCPayServer.Abstractions.Constants;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Client;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Plugins.Monetization.Controllers;

[Authorize(AuthenticationSchemes = AuthenticationSchemes.Cookie, Policy = Policies.CanViewProfile)]
[Area(MonetizationPlugin.Area)]
public class UIUserMonetizationController(
    ApplicationDbContext ctx,
    MonetizationSettings settings,
    PoliciesSettings policies,
    UserManager<ApplicationUser> userManager,
    LinkGenerator linkGenerator
    ) : Controller
{
    [HttpGet("~/monetization/new-user")]
    [AllowAnonymous]
    public async Task<IActionResult> NewUser()
    {
        if (settings is not { OfferingId: { } offeringId, DefaultPlanId: { } defaultPlanId })
            return NotFound();
        if (!policies.EnableRegistration)
            return NotFound();
        var plan = await ctx.Plans.GetPlanFromId(defaultPlanId, offeringId);
        if (plan is null)
            return NotFound();
        var checkout = new PlanCheckoutData()
        {
            PlanId = plan.Id,
            Plan = plan,
            NewSubscriber = true,
            IsTrial = plan.TrialDays > 0,
            BaseUrl = Request.GetRequestBaseUrl()
        };
        ctx.PlanCheckouts.Add(checkout);
        await ctx.SaveChangesAsync();
        return Redirect(linkGenerator.PlanCheckout(checkout.Id, checkout.BaseUrl));
    }

    [HttpGet("~/account/billing")]
    [Authorize(AuthenticationSchemes = $"{AuthenticationSchemes.LimitedLogin},{AuthenticationSchemes.Cookie}")]
    public async Task<IActionResult> ManageBilling()
    {
        if (settings.OfferingId is not { } offeringId)
            return NotFound();
        var userId = userManager.GetUserId(User);
        var sub = await ctx.Subscribers.GetBySelector(offeringId, CustomerSelector.ByIdentity(SubscriberDataExtensions.IdentityType, userId));
        if (sub is null)
            return NotFound();
        var portal = new PortalSessionData()
        {
            BaseUrl = Request.GetRequestBaseUrl(),
            Subscriber = sub
        };
        ctx.PortalSessions.Add(portal);
        await ctx.SaveChangesAsync();
        return Redirect(linkGenerator.SubscriberPortalLink(portal.Id, portal.BaseUrl));
    }
}
