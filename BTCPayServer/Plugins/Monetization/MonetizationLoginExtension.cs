#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Monetization;

public class MonetizationLoginExtension(
    ISettingsAccessor<MonetizationSettings> settings,
    ApplicationDbContextFactory dbContextFactory,
    LinkGenerator linkGenerator) : UserService.LoginExtension
{
    public override async Task Check(UserService.CanLoginContext context)
    {
        if (settings.Settings is { OfferingId: { } offeringId })
        {
            await using var ctx = dbContextFactory.CreateContext();
            var subscriber = await ctx.Subscribers.GetBySelector(offeringId, CustomerSelector.ByIdentity(SubscriberDataExtensions.IdentityType, context.User.Id));
            if (subscriber is null)
                return;

            // The subscriber is not active and suspended => Show the suspension error
            if (subscriber is { IsActive: false, IsSuspended: true })
            {
                var message = subscriber.SuspensionReason is {} reason ? new(reason, reason)  : context.StringLocalizer["Please contact support."];
                context.Failures.Add(new (context.StringLocalizer["Your subscription is suspended. {0}", message]));
                return;
            }

            // The subscriber is in a plan without an access feature
            if (subscriber is { PlanId: { } planId } &&
                !await ctx.Plans.HasEntitlements(planId, MonetizationEntitlments.CanAccess))
            {
                context.Failures.Add(new (context.StringLocalizer["Your plan does not allow you to log in."]));
                var upgrades = await ctx.PlanChanges
                    .Include(o => o.PlanChange)
                    .Where(p => p.PlanId == planId && p.Type == PlanChangeData.ChangeType.Upgrade)
                    .ToArrayAsync();
                if (context.BaseUrl is null)
                    return;

                //  If there is one single upgrade with access feature => Redirect to Portal checkout for upgrade (Hard migration)
                if (upgrades.Length == 1)
                {

                    var upgradePlan = upgrades[0].PlanChange;
                    var checkout = new PlanCheckoutData(subscriber, upgradePlan)
                    {
                        BaseUrl = context.BaseUrl,
                        IsTrial = upgradePlan.TrialDays > 0,
                        OnPay = PlanCheckoutData.OnPayBehavior.HardMigration
                    };
                    ctx.PlanCheckouts.Add(checkout);
                    await ctx.SaveChangesAsync();
                    context.FailedRedirectUrl = linkGenerator.PlanCheckout(checkout.Id, context.BaseUrl);
                }
                // Else => Redirect to Subscriber Portal
                else
                {
                    await RedirectToSubscriberPortal(context, subscriber, ctx);
                }
                return;
            }

            // The subscriber is not active and not suspended => Redirect to subscriber portal
            if (subscriber is { IsActive: false, IsSuspended: false })
            {
                context.Failures.Add(new (context.StringLocalizer["Your subscription is not active."]));
                await RedirectToSubscriberPortal(context, subscriber, ctx);
            }
        }
    }

    private async Task RedirectToSubscriberPortal(UserService.CanLoginContext context, SubscriberData subscriber,
        ApplicationDbContext ctx)
    {
        if (context.BaseUrl is null)
            return;
        var portal = new PortalSessionData()
        {
            SubscriberId = subscriber.Id,
            BaseUrl = context.BaseUrl
        };
        ctx.PortalSessions.Add(portal);
        await ctx.SaveChangesAsync();
        context.FailedRedirectUrl = linkGenerator.SubscriberPortalLink(portal.Id, context.BaseUrl);
    }
}
