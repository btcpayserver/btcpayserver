using BTCPayServer;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Services;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.Wallets;

public class WalletsPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "Wallets";

    public override string Identifier => "BTCPayServer.Plugins.Wallets";
    public override string Name => "Wallets";
    public override string Description => "Pluginized wallet UI surface and setup flows.";

    public override void Execute(IServiceCollection services)
    {
        services.AddTransient<HotwalletSafe>();
        services.AddPolicyDefinitions(
            new PolicyDefinition(
                WalletPolicies.CanManageWallets,
                new PermissionDisplay("Manage wallets", "Allows managing wallets on all your stores, including wallet settings and transactions."),
                new PermissionDisplay("Manage selected stores' wallets", "Allows managing wallets on the selected stores, including wallet settings and transactions."),
                new[]
                {
                    WalletPolicies.CanManageWalletSettings,
                    WalletPolicies.CanManageWalletTransactions
                },
                includedByPermissions: [Policies.CanModifyStoreSettings]),
            new PolicyDefinition(
                WalletPolicies.CanViewWallet,
                new PermissionDisplay("View wallets", "Allows viewing wallets, balances, and transactions."),
                new PermissionDisplay("View selected stores' wallets", "Allows viewing wallets, balances, and transactions on the selected stores.")),
            new PolicyDefinition(
                WalletPolicies.CanManageWalletSettings,
                new PermissionDisplay("Manage wallet settings", "Allows managing wallet settings and metadata."),
                new PermissionDisplay("Manage selected stores' wallet settings", "Allows managing wallet settings and metadata on the selected stores."),
                new[]
                {
                    WalletPolicies.CanViewWallet
                }),
            new PolicyDefinition(
                WalletPolicies.CanManageWalletTransactions,
                new PermissionDisplay("Manage wallet transactions", "Allows managing wallet transactions on all your stores."),
                new PermissionDisplay("Manage selected stores' wallet transactions", "Allows managing wallet transactions on the selected stores."),
                new[]
                {
                    WalletPolicies.CanCreateWalletTransactions,
                    WalletPolicies.CanSignWalletTransactions,
                    WalletPolicies.CanBroadcastWalletTransactions,
                    WalletPolicies.CanCancelWalletTransactions
                }),
            new PolicyDefinition(
                WalletPolicies.CanCreateWalletTransactions,
                new PermissionDisplay("Create wallet transactions", "Allows creating wallet transactions (PSBTs)."),
                new PermissionDisplay("Create wallet transactions", "Allows creating wallet transactions (PSBTs) on the selected stores."),
                new[]
                {
                    WalletPolicies.CanViewWallet
                }),
            new PolicyDefinition(
                WalletPolicies.CanSignWalletTransactions,
                new PermissionDisplay("Sign wallet transactions", "Allows signing wallet transactions (PSBTs)."),
                new PermissionDisplay("Sign wallet transactions", "Allows signing wallet transactions (PSBTs) on the selected stores."),
                new[]
                {
                    WalletPolicies.CanViewWallet
                }),
            new PolicyDefinition(
                WalletPolicies.CanBroadcastWalletTransactions,
                new PermissionDisplay("Broadcast wallet transactions", "Allows broadcasting wallet transactions from BTCPay Server."),
                new PermissionDisplay("Broadcast wallet transactions", "Allows broadcasting wallet transactions from BTCPay Server on the selected stores."),
                new[]
                {
                    WalletPolicies.CanViewWallet
                }),
            new PolicyDefinition(
                WalletPolicies.CanCancelWalletTransactions,
                new PermissionDisplay("Cancel wallet transactions", "Allows canceling wallet transactions."),
                new PermissionDisplay("Cancel wallet transactions", "Allows canceling wallet transactions on the selected stores."),
                new[]
                {
                    WalletPolicies.CanViewWallet
                }));
    }
}
