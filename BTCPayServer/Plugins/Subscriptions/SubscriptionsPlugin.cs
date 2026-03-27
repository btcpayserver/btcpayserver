#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Subscriptions.Controllers;
using BTCPayServer.Security;
using BTCPayServer.Services;
using BTCPayServer.Services.Apps;
using BTCPayServer.Views.UIStoreMembership;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriptionsPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Subscriptions";
    public override string Identifier => "BTCPayServer.Plugins.Subscriptions";
    public override string Name => "Subscriptions";
    public override string Description => "Manage recurring payment plans and subscriptions with customizable offerings, pricing tiers, and billing cycles.";

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("header-nav", "/Plugins/Subscriptions/Views/NavExtension.cshtml");
        services.AddSingleton<AppBaseType, SubscriptionsAppType>();
        services.AddScheduledTask<SubscriptionHostedService>(TimeSpan.FromMinutes(5));
        services.AddSingleton<SubscriptionHostedService>();
        services.AddSingleton<IHostedService>(s => s.GetRequiredService<SubscriptionHostedService>());

        services.AddSingleton(new BuiltInPermissionScopeProvider.RouteValueToStoreIdQuery(
            "offeringId", "SELECT a.\"StoreDataId\" FROM \"Apps\" a JOIN subs_offerings o ON o.app_id=a.\"Id\" WHERE o.id=@id"
        ));

        services.AddScheduledDbScript("Portal Session Cleanup",
            """
            WITH expired_portal_session AS (
                SELECT * FROM subs_portal_sessions
                WHERE expiration < @now - Interval '1 month'
                ORDER BY expiration LIMIT 1000
            ),
             deleted_portal_session AS (
                 DELETE FROM subs_portal_sessions
                 WHERE id IN (SELECT id FROM expired_portal_session)
                 RETURNING *
             )
            SELECT COUNT(*) FROM deleted_portal_session;
            """);

        services.AddScheduledDbScript("Checkout Session Cleanup",
            """
            WITH expired_plan_checkout AS (
                SELECT * FROM subs_plan_checkouts
                WHERE expiration < @now - Interval '1 month'
                ORDER BY expiration LIMIT 1000
            ),
                deleted_plan_checkout AS (
                 DELETE FROM subs_plan_checkouts
                     WHERE id IN (SELECT id FROM expired_plan_checkout)
                     RETURNING *
             )
            SELECT COUNT(*) FROM deleted_plan_checkout;
            """);

        services.AddScheduledDbScript("Credit Refund Cleanup",
            """
            WITH completed_refunds AS (
                SELECT pull_payment_id FROM subs_credit_refunds
                WHERE deducted = false
                ORDER BY pull_payment_id LIMIT 1000
            ),
            deleted AS (
                DELETE FROM subs_credit_refunds
                WHERE pull_payment_id IN (SELECT pull_payment_id FROM completed_refunds)
                RETURNING *
            )
            SELECT COUNT(*) FROM deleted;
            """);

        AddSubscriptionsWebhooks(services);
        AddPolicies(services);
        base.Execute(services);
    }

    private void AddPolicies(IServiceCollection services)
    {
        services.AddPolicyDefinitions(new[]
        {
            new PolicyDefinition(
                SubscriptionsPolicies.CanViewOfferings,
                new PermissionDisplay("View your offerings", "Allows viewing offerings on all your stores."),
                new PermissionDisplay("View your offerings", "Allows viewing offerings on the selected stores.")),
            new PolicyDefinition(
                SubscriptionsPolicies.CanModifyOfferings,
                new PermissionDisplay("Modify your offerings", "Allows modifying offerings on all your stores."),
                new PermissionDisplay("Modify your offerings", "Allows modifying offerings on the selected stores."),
                new[] { SubscriptionsPolicies.CanViewOfferings, SubscriptionsPolicies.CanManageSubscribers, SubscriptionsPolicies.CanCreditSubscribers },
                includedByPermissions: [Policies.CanModifyStoreSettings]),
            new PolicyDefinition(
                SubscriptionsPolicies.CanManageSubscribers,
                new PermissionDisplay("Manage your subscribers", "Allows managing subscribers on all your stores."),
                new PermissionDisplay("Manage your subscribers", "Allows managing subscribers on the selected stores.")),
            new PolicyDefinition(
                SubscriptionsPolicies.CanCreditSubscribers,
                new PermissionDisplay("Credit your subscribers", "Allows crediting subscribers on all your stores."),
                new PermissionDisplay("Credit your subscribers", "Allows crediting subscribers on the selected stores.")),
        });
    }

    private void AddSubscriptionsWebhooks(IServiceCollection services)
    {
        services.AddWebhookTriggerProvider<SubscriberWebhookProvider>();

        var placeHolders = new List<EmailTriggerViewModel.PlaceHolder>()
        {
            new("{Plan.Id}", "Plan ID"),
            new("{Plan.Name}", "Plan name"),
            new("{Offering.Name}", "Offering name"),
            new("{Offering.Id}", "Offering ID"),
            new("{Offering.AppId}", "Offering app ID"),
            new("{Offering.Metadata}*", "Offering metadata"),
            new("{Subscriber.Phase}", "Subscriber phase (Trial, Normal, Grace, Expired)"),
            new("{Subscriber.Email}", "Subscriber email"),
            new("{Subscriber.Metadata}*", "Subscriber metadata"),
            new("{Customer.ExternalRef}", "Customer external reference"),
            new("{Customer.Name}", "Customer name"),
            new("{Customer.Metadata}*", "Customer metadata")
        };

        var viewModels = new List<EmailTriggerViewModel>()
        {
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberCreated,
                Description = "Subscription - New subscriber",
                DefaultEmail = new()
                {
                    To = ["{Subscriber.Email}"],
                    Subject = "Welcome {Customer.Name}!",
                    Body = "Hello {Customer.Name},\n\nThank you for subscribing to our service.\n\nRegards,\n{Store.Name}"
                },
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberCredited,
                Description = "Subscription - Subscriber credited",
                DefaultEmail = new()
                {
                    To = ["{Subscriber.Email}"],
                    Subject = "Your subscription has been credited",
                    Body = "Hello {Customer.Name},\n\nYour subscription has been credited.\n\nRegards,\n{Store.Name}"
                },
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberCharged,
                Description = "Subscription - Subscriber charged",
                DefaultEmail = new()
                {
                    To = ["{Subscriber.Email}"],
                    Subject = "Your subscription payment has been processed",
                    Body = "Hello {Customer.Name},\n\nYour subscription payment for {Plan.Name} has been processed.\n\nRegards,\n{Store.Name}"
                },
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberActivated,
                Description = "Subscription - Subscriber activated",
                DefaultEmail = new()
                {
                    To = ["{Subscriber.Email}"],
                    Subject = "Your subscription is now active",
                    Body = "Hello {Customer.Name},\n\nYour subscription to {Plan.Name} is now active.\n\nRegards,\n{Store.Name}"
                },
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberPhaseChanged,
                Description = "Subscription - Subscriber phase changed",
                DefaultEmail = new()
                {
                    To = ["{Subscriber.Email}"],
                    Subject = "Your subscription phase has changed",
                    Body = "Hello {Customer.Name},\n\nYour subscription phase has been updated to {Subscriber.Phase}.\n\nRegards,\n{Store.Name}"
                },
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberDisabled,
                Description = "Subscription - Subscriber disabled",
                DefaultEmail = new()
                {
                    To = ["{Subscriber.Email}"],
                    Subject = "Your subscription has been disabled",
                    Body = "Hello {Customer.Name},\n\nYour subscription has been disabled.\n\nRegards,\n{Store.Name}"
                },
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.PaymentReminder,
                Description = "Subscription - Payment reminder",
                DefaultEmail = new()
                {
                    To = ["{Subscriber.Email}"],
                    Subject = "Payment reminder for your subscription",
                    Body = "Hello {Customer.Name},\n\nThis is a reminder about your upcoming subscription payment.\n\nRegards,\n{Store.Name}"
                },
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.PlanStarted,
                Description = "Subscription - Plan started",
                DefaultEmail = new()
                {
                    To = ["{Subscriber.Email}"],
                    Subject = "Your subscription plan has started",
                    Body = "Hello {Customer.Name},\n\nYour subscription plan {Plan.Name} has started.\n\nRegards,\n{Store.Name}"
                },
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberNeedUpgrade,
                Description = "Subscription - Need upgrade",
                DefaultEmail = new()
                {
                    To = ["{Subscriber.Email}"],
                    Subject = "Your subscription needs to be upgraded",
                    Body = "Hello {Customer.Name},\n\nYour subscription needs to be upgraded to continue using our service.\n\nRegards,\n{Store.Name}"
                },
                PlaceHolders = placeHolders
            },
        };
        services.AddWebhookTriggerViewModels(viewModels);
    }
}

public class SubscriptionsAppType(
    LinkGenerator linkGenerator,
    IOptions<BTCPayServerOptions> btcPayServerOptions) : AppBaseType(AppType)
{
    public const string AppType = "Subscriptions";

    public class AppConfig
    {
        public string OfferingId { get; set; } = null!;
    }

    public override Task<object?> GetInfo(AppData appData)
        => Task.FromResult<object?>(null);

    public override Task<string> ConfigureLink(AppData app)
    {
        var config = app.GetSettings<AppConfig>();
        return Task.FromResult(linkGenerator.GetPathByAction(nameof(UIOfferingController.Offering),
            "UIOffering", new { storeId = app.Id, offeringId = config?.OfferingId, section = SubscriptionSection.Plans }, btcPayServerOptions.Value.RootPath)!);
    }

    public override Task<string> ViewLink(AppData app)
    {
        var config = app.GetSettings<AppConfig>();
        return Task.FromResult(linkGenerator.GetPathByAction(nameof(UIOfferingController.Offering),
            "UIOffering", new { storeId = app.Id, offeringId = config?.OfferingId, section = SubscriptionSection.Plans }, btcPayServerOptions.Value.RootPath)!);
    }

    public override Task SetDefaultSettings(AppData appData, string defaultCurrency)
    {
        throw new System.NotImplementedException();
    }
}
