#nullable enable
using System;
using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Builder;
using Microsoft.Extensions.DependencyInjection;

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
    }
}
