#nullable enable
using System;
using System.Collections.Generic;
using System.Threading.Tasks;
using BTCPayServer.Client;
using BTCPayServer.Controllers;
using BTCPayServer.Plugins.GlobalSearch.Views;
using Microsoft.AspNetCore.Mvc;

namespace BTCPayServer.Plugins.GlobalSearch;

public class DefaultSearchResultProvider : ISearchResultItemProvider
{
    public async Task ProvideAsync(SearchResultItemProviderContext context)
    {
        var canModifyServer = await context.IsAuthorized(Policies.CanModifyServerSettings);
        var canViewProfile = await context.IsAuthorized(Policies.CanViewProfile);
        var canViewNotifications = await context.IsAuthorized(Policies.CanViewNotificationsForUser);
        var results = context.ItemResults;
        var store = context.Store;
        if (store != null)
        {
            var canViewStoreSettings = await context.IsAuthorized(Policies.CanViewStoreSettings);
            var canModifyStoreSettings = await context.IsAuthorized(Policies.CanModifyStoreSettings);
            var canViewInvoices = await context.IsAuthorized(Policies.CanViewInvoices);
            var canViewReports = await context.IsAuthorized(Policies.CanViewReports);
            var canViewPaymentRequests = await context.IsAuthorized(Policies.CanViewPaymentRequests);
            var canViewPullPayments = await context.IsAuthorized(Policies.CanViewPullPayments);
            var canViewPayouts = await context.IsAuthorized(Policies.CanViewPayouts);

            if (canModifyStoreSettings)
            {
                AddPage(results, "Dashboard", context.Url.Action(nameof(UIStoresController.Dashboard), "UIStores", new { storeId = store.Id }), "Store");
            }

            if (canViewStoreSettings)
            {
                AddPage(results, "Store Settings", context.Url.Action(nameof(UIStoresController.GeneralSettings), "UIStores", new { storeId = store.Id }), "Store",
                    ["General", "Settings", "Branding"]);
                AddPage(results, "Rates", context.Url.Action(nameof(UIStoresController.Rates), "UIStores", new { storeId = store.Id }), "Store", ["Exchange"]);
                AddPage(results, "Checkout Appearance", context.Url.Action(nameof(UIStoresController.CheckoutAppearance), "UIStores", new { storeId = store.Id }),
                    "Store", ["Checkout", "Appearance"]);
                AddPage(results, "Access Tokens", context.Url.Action(nameof(UIStoresController.ListTokens), "UIStores", new { storeId = store.Id }), "Store",
                    ["Tokens"]);
                AddPage(results, "Store Users", context.Url.Action(nameof(UIStoresController.StoreUsers), "UIStores", new { storeId = store.Id }), "Store", ["Users"]);
                AddPage(results, "Store Roles", context.Url.Action(nameof(UIStoresController.ListRoles), "UIStores", new { storeId = store.Id }), "Store", ["Roles"]);
                AddPage(results, "Webhooks", context.Url.Action("Webhooks", "UIStoreWebhooks", new { area = "Webhooks", storeId = store.Id }), "Store", ["Webhooks"]);
                AddPage(results, "Payout Processors", context.Url.Action("ConfigureStorePayoutProcessors", "UIPayoutProcessors", new { storeId = store.Id }), "Store",
                    ["Payout", "Processors"]);
                AddPage(results, "Forms", context.Url.Action("FormsList", "UIForms", new { storeId = store.Id }), "Store", ["Forms"]);
            }

            if (canViewInvoices)
            {
                AddPage(results, "Invoices list", context.Url.Action(nameof(UIInvoiceController.ListInvoices), "UIInvoice", new { storeId = store.Id }), "Payments",
                    ["Payments", "List"]);
                AddPage(results, "Create Invoice", context.Url.Action(nameof(UIInvoiceController.CreateInvoice), "UIInvoice", new { storeId = store.Id }), "Payments",
                    ["Invoice"]);
            }

            if (canViewReports)
            {
                AddPage(results, "Reporting", context.Url.Action(nameof(UIReportsController.StoreReports), "UIReports", new { storeId = store.Id }), "Payments",
                    ["Reports"]);
            }

            if (canViewPaymentRequests)
            {
                AddPage(results, "Payment Requests",
                    context.Url.Action(nameof(UIPaymentRequestController.GetPaymentRequests), "UIPaymentRequest", new { storeId = store.Id }), "Payments",
                    ["Payment", "Requests"]);
            }

            if (canViewPullPayments)
            {
                AddPage(results, "Pull Payments", context.Url.Action("PullPayments", "UIStorePullPayments", new { storeId = store.Id }), "Payments", ["Pull Payments", "Pull"]);
            }

            if (canViewPayouts)
            {
                AddPage(results, "Payouts", context.Url.Action("Payouts", "UIStorePullPayments", new { storeId = store.Id }), "Payments", ["Payouts"]);
            }
        }

        if (canModifyServer)
        {
            AddPage(results, "Server Settings", context.Url.Action(nameof(UIServerController.Policies), "UIServer"), "Server", ["Server", "Settings", "Policies"]);
            AddPage(results, "Users", context.Url.Action(nameof(UIServerController.ListUsers), "UIServer"), "Server", ["Server", "Settings", "Users"]);
            AddPage(results, "Roles", context.Url.Action(nameof(UIServerController.ListRoles), "UIServer"), "Server", ["Server", "Settings", "Roles"]);
            AddPage(results, "Services", context.Url.Action(nameof(UIServerController.Services), "UIServer"), "Server", ["Server", "Settings", "Services"]);
            AddPage(results, "Branding", context.Url.Action(nameof(UIServerController.Branding), "UIServer"), "Server", ["Server", "Settings", "Branding"]);
            AddPage(results, "Translations", context.Url.Action(nameof(UIServerController.ListDictionaries), "UIServer"), "Server", ["Server", "Settings", "Translations"]);
            AddPage(results, "Maintenance", context.Url.Action(nameof(UIServerController.Maintenance), "UIServer"), "Server", ["Server", "Settings", "Maintenance"]);
            AddPage(results, "Logs", context.Url.Action(nameof(UIServerController.LogsView), "UIServer"), "Server", ["Server", "Settings", "Logs"]);
            AddPage(results, "Files", context.Url.Action(nameof(UIServerController.Files), "UIServer"), "Server", ["Server", "Settings", "Files", "Storage"]);

            var pluginsUrl = context.Url.Action(nameof(UIServerController.ListPlugins), "UIServer");
            AddPage(results, "Manage Plugins", pluginsUrl, "Server", ["Plugins"]);
            if (!string.IsNullOrEmpty(pluginsUrl))
            {
                AddPage(results, "Installed Plugins", $"{pluginsUrl}#plugins-installed", "Server", ["Plugins", "Installed"]);
                AddPage(results, "Plugin Directory", $"{pluginsUrl}#plugins-directory", "Server", ["Plugins"]);
            }
        }

        if (canViewProfile)
        {
            AddPage(results, "Manage Account", context.Url.Action(nameof(UIManageController.Index), "UIManage"), "Account", ["Profile", "Account"]);
            AddPage(results, "Password", context.Url.Action(nameof(UIManageController.ChangePassword), "UIManage"), "Account", ["Password"]);
            AddPage(results, "Two-Factor Authentication", context.Url.Action(nameof(UIManageController.TwoFactorAuthentication), "UIManage"), "Account",
                ["2FA Security"]);
            AddPage(results, "API Keys", context.Url.Action(nameof(UIManageController.APIKeys), "UIManage"), "Account", ["API Keys"]);
            AddPage(results, "Notification Settings", context.Url.Action(nameof(UIManageController.NotificationSettings), "UIManage"), "Account", ["Notifications"]);
            AddPage(results, "Login Codes", context.Url.Action(nameof(UIManageController.LoginCodes), "UIManage"), "Account", ["Login", "Codes", "Login Codes"]);
        }

        if (canViewNotifications)
        {
            AddPage(results, "Notifications", context.Url.Action(nameof(UINotificationsController.Index), "UINotifications"), "Account", ["Notifications"]);
        }

        AddPage(results, "Home", context.Url.Action(nameof(UIHomeController.Index), "UIHome"), "General", ["Dashboard", "Overview"]);
        AddPage(results, "Stores list", context.Url.Action(nameof(UIUserStoresController.ListStores), "UIUserStores"), "General", ["Stores", "List"]);
        AddPage(results, "API Docs", context.Url.Action(nameof(UIHomeController.SwaggerDocs), "UIHome"), "General", ["Documentation", "API", "Docs"]);
    }


    private void AddPage(List<ResultItemViewModel> results, string title, string? url, string category, string[]? keywords = null)
    {
        if (url is null)
            throw new ArgumentNullException(nameof(url), "URL cannot be null");
        results.Add(new ResultItemViewModel
        {
            Category = category,
            Title = title,
            Url = url,
            Keywords = keywords ?? []
        });
    }
}
