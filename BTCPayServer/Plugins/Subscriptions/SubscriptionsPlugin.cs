#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Plugins.Emails.Views;
using BTCPayServer.Plugins.Subscriptions.Controllers;
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

        AddSubscriptionsWebhooks(services);

        base.Execute(services);
    }

    private void AddSubscriptionsWebhooks(IServiceCollection services)
    {
        services.AddWebhookTriggerProvider<SubscriberWebhookProvider>();

        var placeHolders = new List<EmailTriggerViewModel.PlaceHolder>()
        {
            new ("{Plan.Id}", "Plan ID"),
            new ("{Plan.Name}", "Plan name"),
            new ("{Offering.Name}", "Offering name"),
            new ("{Offering.Id}", "Offering ID"),
            new ("{Offering.AppId}", "Offering app ID"),
            new ("{Offering.Metadata}*", "Offering metadata"),
            new ("{Subscriber.Phase}", "Subscriber phase"),
            new ("{Subscriber.Email}", "Subscriber email"),
            new ("{Customer.ExternalRef}", "Customer external reference"),
            new ("{Customer.Name}", "Customer name"),
            new ("{Customer.Metadata}*", "Customer metadata")

        }.AddStoresPlaceHolders();

        var viewModels = new List<EmailTriggerViewModel>()
        {
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberCreated,
                Description = "Subscription - New subscriber",
                SubjectExample = "Welcome {Customer.Name}!",
                BodyExample = "Hello {Customer.Name},\n\nThank you for subscribing to our service.\n\nRegards,\n{Store.Name}",
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberCredited,
                Description = "Subscription - Subscriber credited",
                SubjectExample = "Your subscription has been credited",
                BodyExample = "Hello {Customer.Name},\n\nYour subscription has been credited.\n\nRegards,\n{Store.Name}",
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberCharged,
                Description = "Subscription - Subscriber charged",
                SubjectExample = "Your subscription payment has been processed",
                BodyExample = "Hello {Customer.Name},\n\nYour subscription payment for {Plan.Name} has been processed.\n\nRegards,\n{Store.Name}",
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberActivated,
                Description = "Subscription - Subscriber activated",
                SubjectExample = "Your subscription is now active",
                BodyExample = "Hello {Customer.Name},\n\nYour subscription to {Plan.Name} is now active.\n\nRegards,\n{Store.Name}",
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberPhaseChanged,
                Description = "Subscription - Subscriber phase changed",
                SubjectExample = "Your subscription phase has changed",
                BodyExample = "Hello {Customer.Name},\n\nYour subscription phase has been updated to {Subscriber.Phase}.\n\nRegards,\n{Store.Name}",
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberDisabled,
                Description = "Subscription - Subscriber disabled",
                SubjectExample = "Your subscription has been disabled",
                BodyExample = "Hello {Customer.Name},\n\nYour subscription has been disabled.\n\nRegards,\n{Store.Name}",
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.PaymentReminder,
                Description = "Subscription - Payment reminder",
                SubjectExample = "Payment reminder for your subscription",
                BodyExample = "Hello {Customer.Name},\n\nThis is a reminder about your upcoming subscription payment.\n\nRegards,\n{Store.Name}",
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.PlanStarted,
                Description = "Subscription - Plan started",
                SubjectExample = "Your subscription plan has started",
                BodyExample = "Hello {Customer.Name},\n\nYour subscription plan {Plan.Name} has started.\n\nRegards,\n{Store.Name}",
                PlaceHolders = placeHolders
            },
            new()
            {
                Trigger = WebhookSubscriptionEvent.SubscriberNeedUpgrade,
                Description = "Subscription - Need upgrade",
                SubjectExample = "Your subscription needs to be upgraded",
                BodyExample = "Hello {Customer.Name},\n\nYour subscription needs to be upgraded to continue using our service.\n\nRegards,\n{Store.Name}",
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
