using BTCPayServer.Abstractions.Models;
using BTCPayServer.Security;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Multisig;

public class MultisigPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Multisig";
    public override string Identifier => "BTCPayServer.Plugins.Multisig";
    public override string Name => "Multisig";
    public override string Description => "Pluginized multisig setup and signer key collection flows.";

    public override void Execute(IServiceCollection services)
    {
        services.AddSingleton(new BuiltInPermissionScopeProvider.RouteValueToStoreIdQuery(
            "multisigSetupId",
            """
            SELECT "store_id"
            FROM "multisig_setups"
            WHERE "id" = @id
            LIMIT 1
            """));
        services.AddSingleton<Services.MultisigService>();
        services.AddSingleton<Services.MultisigNotificationService>();
        services.AddHostedService<HostedServices.MultisigEmailTriggerHostedService>();
        services.AddSearchResultItemProvider<MultisigSearchResultProvider>();
        foreach (var trigger in MultisigEmailTriggers.GetViewModels())
        {
            services.AddSingleton(trigger);
        }

        services.AddUIExtension("store-onchain-wallet-setup", "/Plugins/Multisig/Views/SetupWalletCard.cshtml");
        services.AddUIExtension("dashboard-setup-guide-wallet", "/Plugins/Multisig/Views/DashboardSetupGuideExtension.cshtml");
        services.AddScheduledDbScript("Multisig Setup Cleanup", "DELETE FROM multisig_setups WHERE expires_at < NOW()");
    }
}
