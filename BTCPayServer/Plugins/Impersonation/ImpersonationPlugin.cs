using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Security;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Impersonation;

public class ImpersonationPlugin : BaseBTCPayServerPlugin
{
    public const string CanImpersonateUser = "btcpay.impersonation.canimpersonate";
    public const string Area = "Impersonation";
    public override string Identifier => "BTCPayServer.Plugins.Impersonation";
    public override string Name => "Impersonation";
    public override string Description => "Allow user impersonation";

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("layout-banner", "/Plugins/Impersonation/Views/Banner.cshtml");
        services.AddUIExtension("user-nav", "/Plugins/Impersonation/Views/UserNav.cshtml");
        services.AddSingleton<UserLoginCodeService>();
        services.AddScoped<ImpersonationContext>();
        services.AddPolicyDefinitions(new[]
        {
            new PolicyDefinition(
                CanImpersonateUser,
                new PermissionDisplay("Can impersonate users", "Allows user impersonation."),
                new PermissionDisplay("Can impersonate the selected users", "Allows impersonation of the selected users."))
        });
        services.AddSingleton<IPermissionHandler, ImpersonationPermissionHandler>();
    }
}
