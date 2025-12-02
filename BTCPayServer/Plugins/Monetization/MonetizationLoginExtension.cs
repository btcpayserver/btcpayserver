#nullable enable
using System;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Mvc;
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
                RedirectToManageBilling(context);
                return;
            }

            // The subscriber is in a plan without an access feature
            if (subscriber is { PlanId: { } planId } &&
                !await ctx.Plans.HasFeature(planId, MonetizationFeatures.CanAccess))
            {
                context.Failures.Add(new (context.StringLocalizer["Your plan does not allow you to log in."]));
                if (await CanChangePlan(ctx, planId))
                    RedirectToManageBilling(context);
            }

            // The subscriber is not active and not suspended => Redirect to subscriber portal
            if (subscriber is { IsActive: false, IsSuspended: false })
            {
                context.Failures.Add(new (context.StringLocalizer["Your subscription is not active."]));
                RedirectToManageBilling(context);
            }
        }
    }

    private static Task<bool> CanChangePlan(ApplicationDbContext ctx, string planId)
    =>  ctx.PlanChanges
        .Where(p => p.PlanId == planId)
        .AnyAsync();

    private void RedirectToManageBilling(UserService.CanLoginContext context)
    {
        if (context.BaseUrl is not null)
            context.FailedRedirectUrl = linkGenerator.UserManageBillingLink(context.BaseUrl, true);
    }
}
