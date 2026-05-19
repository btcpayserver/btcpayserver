using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Plugins.GlobalSearch;
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
        services.AddUIExtension("user-nav", "/Plugins/Impersonation/Views/UserNav.cshtml");
        services.AddSingleton<UserLoginCodeService>();
        services.AddPolicyDefinitions(new[]
        {
            new PolicyDefinition(
                CanImpersonateUser,
                new PermissionDisplay("Can impersonate users", "Allows user impersonation."),
                new PermissionDisplay("Can impersonate the selected users", "Allows impersonation of the selected users."))
        });
        services.AddSingleton<IPermissionHandler, ImpersonationPermissionHandler>();

        services.AddStaticSearch(new ActionResultItemViewModel()
        {
            RequiredPolicy = Policies.CanViewProfile,
            Title = "Log another device from a QR Code",
            Action = nameof(UIImpersonationController.LoginCodes),
            Controller = "UIImpersonation",
            Values = ctx => new { area = Area },
            Category = "Account",
            Keywords = ["Login", "Codes", "Login Codes", "QR", "Device", "Impersonate"]
        });
    }
}
