using BTCPayServer.Abstractions.Models;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Plugins.GlobalSearch;

public class GlobalSearchPlugin : BaseBTCPayServerPlugin
{
    public const string Area = "GlobalSearch";
    public override string Identifier => "BTCPayServer.Plugins.GlobalSearch";
    public override string Name => "GlobalSearch";
    public override string Description => "Add global search feature to your server.";

    public override void Execute(IServiceCollection services)
    {
        services.AddUIExtension("global-nav", "/Plugins/GlobalSearch/Views/NavExtension.cshtml");
        services.AddSearchResultItemProvider<StaticSearchResultProvider>();
        services.AddSearchResultItemProvider<InvoiceSearchResultProvider>();
        services.AddScoped<SearchResultItemProviders>();
        services.AddTranslationProvider<StaticSearchResultProvider.TranslationProvider>();
        AddDefaultStaticSearch(services);
    }

    private static void AddDefaultStaticSearch(IServiceCollection services)
    {
        services.AddStaticSearch([
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyStoreSettings,
                Title = "Dashboard",
                Action = nameof(UIStoresController.Dashboard),
                Controller = "UIStores",
                Values = ctx => new { storeId = ctx.Store!.Id },
                Category = "Store"
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewStoreSettings,
                Title = "Go to the store's settings",
                Action = nameof(UIStoresController.GeneralSettings),
                Controller = "UIStores",
                Values = ctx => new { storeId = ctx.Store!.Id },
                Category = "Store",
                Keywords = ["Settings", "Branding"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewStoreSettings,
                Title = "Configure exchange rates",
                Action = nameof(UIStoresController.Rates),
                Controller = "UIStores",
                Values = ctx => new { storeId = ctx.Store!.Id },
                Category = "Store",
                Keywords = ["Exchange", "Configure"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewStoreSettings,
                Title = "Configure the appearance of the checkout",
                Action = nameof(UIStoresController.CheckoutAppearance),
                Controller = "UIStores",
                Values = ctx => new { storeId = ctx.Store!.Id },
                Category = "Store",
                Keywords = ["Checkout", "Appearance", "Configure"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewStoreSettings,
                Title = "View store users",
                Action = nameof(UIStoresController.StoreUsers),
                Controller = "UIStores",
                Values = ctx => new { storeId = ctx.Store!.Id },
                Category = "Store",
                Keywords = ["Users", "View"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewStoreSettings,
                Title = "View store roles",
                Action = nameof(UIStoresController.ListRoles),
                Controller = "UIStores",
                Values = ctx => new { storeId = ctx.Store!.Id },
                Category = "Store",
                Keywords = ["Roles", "View"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewStoreSettings,
                Title = "Configure the payout processors",
                Action = "ConfigureStorePayoutProcessors",
                Controller = "UIPayoutProcessors",
                Values = ctx => new { storeId = ctx.Store!.Id },
                Category = "Store",
                Keywords = ["Payout", "Processors", "Configure"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewPaymentRequests,
                Title = "View the payment requests",
                Action = nameof(UIPaymentRequestController.GetPaymentRequests),
                Controller = "UIPaymentRequest",
                Values = ctx => new { storeId = ctx.Store!.Id },
                Category = "Payments",
                Keywords = ["Payment", "Requests", "View"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewPullPayments,
                Title = "View the pull payments",
                Action = "PullPayments",
                Controller = "UIStorePullPayments",
                Values = ctx => new { storeId = ctx.Store!.Id },
                Category = "Payments",
                Keywords = ["Pull Payments", "Pull", "View"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewPayouts,
                Title = "View the payouts",
                Action = "Payouts",
                Controller = "UIStorePullPayments",
                Values = ctx => new { storeId = ctx.Store!.Id },
                Category = "Payments",
                Keywords = ["Payouts", "View"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "Configure the server settings",
                Action = nameof(UIServerController.Policies),
                Controller = "UIServer",
                Category = "Server",
                Keywords = ["Server", "Settings", "Policies", "Configure"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "View server's registered users",
                Action = nameof(UIServerController.ListUsers),
                Controller = "UIServer",
                Category = "Server",
                Keywords = ["Server", "Settings", "Users", "View"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "View predefined store roles",
                Action = nameof(UIServerController.ListRoles),
                Controller = "UIServer",
                Category = "Server",
                Keywords = ["Server", "Settings", "Roles", "View"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "View access to external services",
                Action = nameof(UIServerController.Services),
                Controller = "UIServer",
                Category = "Server",
                Keywords = ["Server", "Settings", "Services", "View"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "Configure the branding appearance of the server",
                Action = nameof(UIServerController.Branding),
                Controller = "UIServer",
                Category = "Server",
                Keywords = ["Server", "Settings", "Branding", "Configure"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "Go to the maintenance page",
                Action = nameof(UIServerController.Maintenance),
                Controller = "UIServer",
                Category = "Server",
                Keywords = ["Server", "Settings", "Maintenance"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "Update the server",
                Action = nameof(UIServerController.Maintenance),
                Controller = "UIServer",
                Category = "Server",
                Keywords = ["Server", "Settings", "Maintenance"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "View the logs",
                Action = nameof(UIServerController.LogsView),
                Controller = "UIServer",
                Category = "Server",
                Keywords = ["Server", "Settings", "Logs", "View"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "Configure a file provider",
                Action = nameof(UIServerController.Files),
                Controller = "UIServer",
                Category = "Server",
                Keywords = ["Server", "Settings", "Files", "Storage", "Configure"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanModifyServerSettings,
                Title = "Install, update, and configure plugins",
                Action = nameof(UIServerController.ListPlugins),
                Controller = "UIServer",
                Category = "Server",
                Keywords = ["Plugins", "Configure", "Update", "Install"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewProfile,
                Title = "Manage your account",
                Action = nameof(UIManageController.Index),
                Controller = "UIManage",
                Category = "Account",
                Keywords = ["Profile", "Account", "Manage"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewProfile,
                Title = "Change your password",
                Action = nameof(UIManageController.ChangePassword),
                Controller = "UIManage",
                Category = "Account",
                Keywords = ["Password", "Change"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewProfile,
                Title = "Configure Two-Factor Authentication",
                Action = nameof(UIManageController.TwoFactorAuthentication),
                Controller = "UIManage",
                Category = "Account",
                Keywords = ["2FA Security", "Two-Factor", "Authentication"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewProfile,
                Title = "Configure Passkey Authentication",
                Action = nameof(UIManageController.Passkeys),
                Controller = "UIManage",
                Category = "Account",
                Keywords = ["Passkey", "Authentication"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewProfile,
                Title = "Manage API Keys",
                Action = nameof(UIManageController.APIKeys),
                Controller = "UIManage",
                Category = "Account",
                Keywords = ["API", "Keys"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewProfile,
                Title = "Manage the notification settings",
                Action = nameof(UIManageController.NotificationSettings),
                Controller = "UIManage",
                Category = "Account",
                Keywords = ["Notifications", "Manage"]
            },
            new ActionResultItemViewModel
            {
                RequiredPolicy = Policies.CanViewNotificationsForUser,
                Title = "View your notifications",
                Action = nameof(UINotificationsController.Index),
                Controller = "UINotifications",
                Category = "Account",
                Keywords = ["Notifications", "View"]
            },
            new ActionResultItemViewModel
            {
                Title = "Go to the dashboard",
                Action = nameof(UIHomeController.Index),
                Controller = "UIHome",
                Category = "General",
                Keywords = ["Dashboard", "Overview"]
            },
            new ActionResultItemViewModel
            {
                Title = "View all the stores",
                Action = nameof(UIUserStoresController.ListStores),
                Controller = "UIUserStores",
                Category = "Store",
                Keywords = ["Stores", "List", "View"]
            },
            new ActionResultItemViewModel
            {
                Title = "Browse the API documentation",
                Action = nameof(UIHomeController.SwaggerDocs),
                Controller = "UIHome",
                Category = "General",
                Keywords = ["Documentation", "API", "Docs", "Browse"]
            }
        ]);
    }
}
