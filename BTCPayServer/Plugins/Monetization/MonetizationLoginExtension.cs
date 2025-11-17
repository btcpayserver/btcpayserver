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

            PlanData? upgradePlan = null;
            var canRedirect = false;
            if (subscriber is { IsActive: false, IsSuspended: false })
            {
                context.Failures.Add(new (context.StringLocalizer["Your subscription is not active."]));
                canRedirect = true;
            }
            else if (subscriber is { IsActive: false, IsSuspended: true })
            {
                var message = subscriber.SuspensionReason is {} reason ? new(reason, reason)  : context.StringLocalizer["Please contact support."];
                context.Failures.Add(new (context.StringLocalizer["Your subscription is suspended. {0}", message]));
                canRedirect = false;
            }

            if (subscriber is { PlanId: { } planId } &&
                !await ctx.Plans.HasEntitlements(planId, MonetizationEntitlments.CanLogin))
            {
                context.Failures.Add(new (context.StringLocalizer["Your current plan does not allow you to log in."]));
                var upgrades = await ctx.PlanChanges
                    .Include(o => o.PlanChange)
                    .Where(p => p.PlanId == planId && p.Type == PlanChangeData.ChangeType.Upgrade)
                    .ToArrayAsync();
                if (upgrades.Length == 1)
                    upgradePlan = upgrades[0].PlanChange;
                canRedirect = upgrades.Length > 0;
            }

            if (canRedirect && context.BaseUrl is not null)
            {
                if (upgradePlan is null)
                {
                    var portal = new PortalSessionData()
                    {
                        SubscriberId = subscriber.Id,
                        BaseUrl = context.BaseUrl
                    };
                    ctx.PortalSessions.Add(portal);
                    await ctx.SaveChangesAsync();
                    context.RedirectUrl = linkGenerator.SubscriberPortalLink(portal.Id, context.BaseUrl);
                }
                else
                {
                    var checkout = new PlanCheckoutData(subscriber, upgradePlan)
                    {
                        BaseUrl = context.BaseUrl,
                        IsTrial = upgradePlan.TrialDays > 0,
                        OnPay = PlanCheckoutData.OnPayBehavior.HardMigration
                    };
                    ctx.PlanCheckouts.Add(checkout);
                    await ctx.SaveChangesAsync();
                    context.RedirectUrl = linkGenerator.PlanCheckout(checkout.Id, context.BaseUrl);
                }
            }
        }
    }
}
