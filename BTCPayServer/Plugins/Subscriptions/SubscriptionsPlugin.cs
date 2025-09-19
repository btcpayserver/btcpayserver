#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Plugins.PointOfSale;
using BTCPayServer.Plugins.PointOfSale.Controllers;
using BTCPayServer.Services.Apps;
using Microsoft.AspNetCore.Routing;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriptionsPlugin : BaseBTCPayServerPlugin
{
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
        base.Execute(services);
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
        return Task.FromResult(linkGenerator.GetPathByAction(nameof(UIStoreSubscriptionsController.Offering),
            "UIStoreSubscriptions", new { storeId = app.Id, offeringId = config?.OfferingId }, btcPayServerOptions.Value.RootPath)!);
    }

    public override Task<string> ViewLink(AppData app)
    {
        var config = app.GetSettings<AppConfig>();
        return Task.FromResult(linkGenerator.GetPathByAction(nameof(UIStoreSubscriptionsController.Offering),
            "UIStoreSubscriptions", new { storeId = app.Id, offeringId = config?.OfferingId }, btcPayServerOptions.Value.RootPath)!);
    }

    public override Task SetDefaultSettings(AppData appData, string defaultCurrency)
    {
        throw new System.NotImplementedException();
    }
}
