#nullable enable
using System;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.Monetization;

public class MonetizationPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Monetization";
    public override string Identifier => "BTCPayServer.Plugins.Monetization";
    public override string Name => "Monetization";
    public override string Description => "Manage monetization of your server.";

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("server-nav", "/Plugins/Monetization/Views/NavExtension.cshtml");
        services.AddSingleton<MonetizationHostedService>();
        services.AddSingleton<IHostedService, MonetizationHostedService>(o => o.GetRequiredService<MonetizationHostedService>());
        services.AddSettingsAccessor<MonetizationSettings>();
        services.AddSingleton<UserService.LoginExtension, MonetizationLoginExtension>();
    }
}
