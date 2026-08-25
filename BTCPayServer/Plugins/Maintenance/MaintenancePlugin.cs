using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;

namespace BTCPayServer.Plugins.Maintenance;

public class MaintenancePlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Maintenance";
    public override string Identifier => "BTCPayServer.Plugins.Maintenance";
    public override string Name => "Maintenance";
    public override string Description => "Manage BTCPay Server maintenance actions.";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton<CheckHostCommandsHostedService>();
        services.AddSingleton<IHostedService, CheckHostCommandsHostedService>(o => o.GetRequiredService<CheckHostCommandsHostedService>());
        services.AddUIExtension("server-nav", "/Plugins/Maintenance/Views/NavExtension.cshtml");
        services.AddSearchResultItemProvider<MaintenanceSearchResultProvider>();
        services.AddTranslationProvider<MaintenanceSearchResultProvider.TranslationProvider>();
    }
}
