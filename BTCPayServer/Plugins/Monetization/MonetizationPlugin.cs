#nullable enable
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;
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
        services.AddUIExtension("user-nav", "/Plugins/Monetization/Views/UserNavExtension.cshtml");
        services.AddSingleton<MonetizationHostedService>();
        services.AddSingleton<IHostedService, MonetizationHostedService>(o => o.GetRequiredService<MonetizationHostedService>());
        services.AddSettingsAccessor<MonetizationSettings>();
        services.AddSingleton<UserService.LoginExtension, MonetizationLoginExtension>();

        services.AddSingleton<IEmailTriggerViewModelTransformer, MonetizationEmailTriggerTransformer>();
        services.AddSingleton<IEmailTriggerEventTransformer, MonetizationEmailTriggerTransformer>();
        services.AddDefaultTranslations(MonetizationEmailTriggerTransformer.TranslatedStrings);
    }
}
