using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Multisig;

public class MultisigPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Multisig";
    public override string Identifier => "BTCPayServer.Plugins.Multisig";
    public override string Name => "Multisig";
    public override string Description => "Pluginized multisig setup and invite flows.";

    public override void Execute(IServiceCollection services)
    {
        services.AddScoped<Services.MultisigService>();
        services.AddScoped<Services.MultisigNotificationService>();
        services.AddHostedService<HostedServices.MultisigPendingTransactionHostedService>();

        services.AddUIExtension("store-onchain-wallet-setup", "/Plugins/Multisig/Views/SetupWalletCard.cshtml");
        services.AddUIExtension("dashboard-setup-guide-wallet", "/Plugins/Multisig/Views/DashboardSetupGuideExtension.cshtml");
        services.AddUIExtension("wallets-top", "/Plugins/Multisig/Views/WalletsTopExtension.cshtml");
    }
}
