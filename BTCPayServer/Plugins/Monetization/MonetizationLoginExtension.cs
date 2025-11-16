using System;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Routing;

namespace BTCPayServer.Plugins.Monetization;

public class MonetizationLoginExtension(
    ISettingsAccessor<MonetizationSettings> settings,
    ApplicationDbContextFactory dbContextFactory,
    SubscriptionHostedService subscriptionHostedService,
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

            var failureCountBefore = context.Failures.Count;
            if (subscriber is { IsActive: false, IsSuspended: false })
            {
                context.Failures.Add(new (context.StringLocalizer["Your subscription is not active."]));
            }
            else if (subscriber is { IsActive: false, IsSuspended: true })
            {
                var message = subscriber.SuspensionReason is {} reason ? new(reason, reason)  : context.StringLocalizer["Please contact support."];
                context.Failures.Add(new (context.StringLocalizer["Your subscription is suspended. {0}", message]));
            }

            if (subscriber is { PlanId: { } planId } &&
                !await ctx.Plans.HasEntitlements(planId, MonetizationEntitlments.CanLogin))
            {
                context.Failures.Add(new (context.StringLocalizer["Your current plan does not allow you to log in."]));
            }

            var failed = failureCountBefore != context.Failures.Count;
            if (failed && context.BaseUrl is not null)
            {
                var portal = new PortalSessionData()
                {
                    SubscriberId = subscriber.Id,
                    Expiration = DateTimeOffset.UtcNow + TimeSpan.FromHours(1.0),
                    BaseUrl = context.BaseUrl
                };
                ctx.PortalSessions.Add(portal);
                await ctx.SaveChangesAsync();
                context.RedirectUrl = linkGenerator.SubscriberPortalLink(portal.Id, context.BaseUrl);
            }
        }
    }
}
