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
[Route("account/subscription")]
[Area(MonetizationPlugin.Area)]
public class UIUserMonetizationController(
    ApplicationDbContext ctx,
    ISettingsAccessor<MonetizationSettings> monetizationSettingsAccessor,
    UserManager<ApplicationUser> userManager,
    LinkGenerator linkGenerator
    ) : Controller
{
    [HttpGet]
    public async Task<IActionResult> ManageSubscription()
    {
        if (monetizationSettingsAccessor.Settings.OfferingId is not { } offeringId)
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
