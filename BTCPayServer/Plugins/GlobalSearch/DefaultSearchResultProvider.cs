#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.GlobalSearch.Views;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.GlobalSearch;

public class DefaultSearchResultProvider : ISearchResultItemProvider
{
    public async Task ProvideAsync(SearchResultItemProviderContext context, CancellationToken cancellationToken)
    {
        if (context.UserQuery is not null)
            return;

        var canModifyServer = await context.IsAuthorized(Policies.CanModifyServerSettings);
        var canViewProfile = await context.IsAuthorized(Policies.CanViewProfile);
        var canViewNotifications = await context.IsAuthorized(Policies.CanViewNotificationsForUser);
        var results = context.ItemResults;
        var store = context.Store;
        if (store != null)
        {
            var canViewStoreSettings = await context.IsAuthorized(Policies.CanViewStoreSettings);
            var canModifyStoreSettings = await context.IsAuthorized(Policies.CanModifyStoreSettings);

            var canViewPaymentRequests = await context.IsAuthorized(Policies.CanViewPaymentRequests);
            var canViewPullPayments = await context.IsAuthorized(Policies.CanViewPullPayments);
            var canViewPayouts = await context.IsAuthorized(Policies.CanViewPayouts);

            if (canModifyStoreSettings)
            {
                AddPage(results, "Dashboard", context.Url.Action(nameof(UIStoresController.Dashboard), "UIStores", new { storeId = store.Id }), "Store");
            }

            if (canViewStoreSettings)
            {
                AddPage(results, "Go to the store's settings", context.Url.Action(nameof(UIStoresController.GeneralSettings), "UIStores", new { storeId = store.Id }), "Store",
                    ["Settings", "Branding"]);
                AddPage(results, "Configure exchange rates", context.Url.Action(nameof(UIStoresController.Rates), "UIStores", new { storeId = store.Id }), "Store", ["Exchange", "Configure"]);
                AddPage(results, "Configure the appearance of the checkout", context.Url.Action(nameof(UIStoresController.CheckoutAppearance), "UIStores", new { storeId = store.Id }),
                    "Store", ["Checkout", "Appearance", "Configure"]);
                AddPage(results, "View store users", context.Url.Action(nameof(UIStoresController.StoreUsers), "UIStores", new { storeId = store.Id }), "Store", ["Users", "View"]);
                AddPage(results, "View store roles", context.Url.Action(nameof(UIStoresController.ListRoles), "UIStores", new { storeId = store.Id }), "Store", ["Roles", "View"]);
                AddPage(results, "Configure the payout processors", context.Url.Action("ConfigureStorePayoutProcessors", "UIPayoutProcessors", new { storeId = store.Id }), "Store",
                    ["Payout", "Processors", "Configure"]);
            }

            if (canViewPaymentRequests)
            {
                AddPage(results, "View the payment requests",
                    context.Url.Action(nameof(UIPaymentRequestController.GetPaymentRequests), "UIPaymentRequest", new { storeId = store.Id }), "Payments",
                    ["Payment", "Requests", "View"]);
            }

            if (canViewPullPayments)
            {
                AddPage(results, "View the pull payments", context.Url.Action("PullPayments", "UIStorePullPayments", new { storeId = store.Id }), "Payments", ["Pull Payments", "Pull", "View"]);
            }

            if (canViewPayouts)
            {
                AddPage(results, "View the payouts", context.Url.Action("Payouts", "UIStorePullPayments", new { storeId = store.Id }), "Payments", ["Payouts", "View"]);
            }
        }

        if (canModifyServer)
        {
            AddPage(results, "Configure the server settings", context.Url.Action(nameof(UIServerController.Policies), "UIServer"), "Server", ["Server", "Settings", "Policies", "Configure"]);
            AddPage(results, "View server's registered users", context.Url.Action(nameof(UIServerController.ListUsers), "UIServer"), "Server", ["Server", "Settings", "Users", "View"]);
            AddPage(results, "View predefined store roles", context.Url.Action(nameof(UIServerController.ListRoles), "UIServer"), "Server", ["Server", "Settings", "Roles", "View"]);
            AddPage(results, "View access to external services", context.Url.Action(nameof(UIServerController.Services), "UIServer"), "Server", ["Server", "Settings", "Services", "View"]);
            AddPage(results, "Configure the branding appearance of the server", context.Url.Action(nameof(UIServerController.Branding), "UIServer"), "Server", ["Server", "Settings", "Branding", "Configure"]);
            AddPage(results, "Go to the maintenance page", context.Url.Action(nameof(UIServerController.Maintenance), "UIServer"), "Server", ["Server", "Settings", "Maintenance"]);
            AddPage(results, "Update the server", context.Url.Action(nameof(UIServerController.Maintenance), "UIServer"), "Server", ["Server", "Settings", "Maintenance"]);
            AddPage(results, "View the logs", context.Url.Action(nameof(UIServerController.LogsView), "UIServer"), "Server", ["Server", "Settings", "Logs", "View"]);
            AddPage(results, "Configure a file provider", context.Url.Action(nameof(UIServerController.Files), "UIServer"), "Server", ["Server", "Settings", "Files", "Storage", "Configure"]);

            var pluginsUrl = context.Url.Action(nameof(UIServerController.ListPlugins), "UIServer");
            AddPage(results, "Install, update, and configure plugins", pluginsUrl, "Server", ["Plugins", "Configure", "Update", "Install"]);
        }

        if (canViewProfile)
        {
            AddPage(results, "Manage your account", context.Url.Action(nameof(UIManageController.Index), "UIManage"), "Account", ["Profile", "Account", "Manage"]);
            AddPage(results, "Change your password", context.Url.Action(nameof(UIManageController.ChangePassword), "UIManage"), "Account", ["Password", "Change"]);
            AddPage(results, "Configure Two-Factor Authentication", context.Url.Action(nameof(UIManageController.TwoFactorAuthentication), "UIManage"), "Account",
                ["2FA Security", "Two-Factor", "Authentication"]);
            AddPage(results, "Manage API Keys", context.Url.Action(nameof(UIManageController.APIKeys), "UIManage"), "Account", ["API", "Keys"]);
            AddPage(results, "Manage the notification settings", context.Url.Action(nameof(UIManageController.NotificationSettings), "UIManage"), "Account", ["Notifications", "Manage"]);
            AddPage(results, "Log another device from a QR Code", context.Url.Action(nameof(UIManageController.LoginCodes), "UIManage"), "Account", ["Login", "Codes", "Login Codes", "QR", "Device"]);
        }

        if (canViewNotifications)
        {
            AddPage(results, "View your notifications", context.Url.Action(nameof(UINotificationsController.Index), "UINotifications"), "Account", ["Notifications", "View"]);
        }

        AddPage(results, "Go to the dashboard", context.Url.Action(nameof(UIHomeController.Index), "UIHome"), "General", ["Dashboard", "Overview"]);
        AddPage(results, "View all the stores", context.Url.Action(nameof(UIUserStoresController.ListStores), "UIUserStores"), "Store", ["Stores", "List", "View"]);
        AddPage(results, "Browse the API documentation", context.Url.Action(nameof(UIHomeController.SwaggerDocs), "UIHome"), "General", ["Documentation", "API", "Docs", "Browse"]);
    }


    private void AddPage(List<ResultItemViewModel> results, string title, string? url, string category, string[]? keywords = null, string? subtitle = null)
    {
        if (url is null)
            throw new ArgumentNullException(nameof(url), "URL cannot be null");
        results.Add(new ResultItemViewModel
        {
            Category = category,
            Title = title,
            Url = url,
            Keywords = keywords ?? [],
            SubTitle = subtitle
        });
    }
}
