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
                    "general settings branding");
                AddPage(results, "Rates", context.Url.Action(nameof(UIStoresController.Rates), "UIStores", new { storeId = store.Id }), "Store", "Exchange");
                AddPage(results, "Checkout Appearance", context.Url.Action(nameof(UIStoresController.CheckoutAppearance), "UIStores", new { storeId = store.Id }),
                    "Store", "checkout");
                AddPage(results, "Access Tokens", context.Url.Action(nameof(UIStoresController.ListTokens), "UIStores", new { storeId = store.Id }), "Store",
                    "tokens api");
                AddPage(results, "Store Users", context.Url.Action(nameof(UIStoresController.StoreUsers), "UIStores", new { storeId = store.Id }), "Store", "users");
                AddPage(results, "Store Roles", context.Url.Action(nameof(UIStoresController.ListRoles), "UIStores", new { storeId = store.Id }), "Store", "roles");
                AddPage(results, "Webhooks", context.Url.Action("Webhooks", "UIStoreWebhooks", new { area = "Webhooks", storeId = store.Id }), "Store", "webhooks");
                AddPage(results, "Payout Processors", context.Url.Action("ConfigureStorePayoutProcessors", "UIPayoutProcessors", new { storeId = store.Id }), "Store",
                    "payout processors");
                AddPage(results, "Forms", context.Url.Action("FormsList", "UIForms", new { storeId = store.Id }), "Store", "forms");
            }

            if (canViewInvoices)
            {
                AddPage(results, "Invoices", context.Url.Action(nameof(UIInvoiceController.ListInvoices), "UIInvoice", new { storeId = store.Id }), "Payments",
                    "payments");
                AddPage(results, "Create Invoice", context.Url.Action(nameof(UIInvoiceController.CreateInvoice), "UIInvoice", new { storeId = store.Id }), "Payments",
                    "invoice");
            }

            if (canViewReports)
            {
                AddPage(results, "Reporting", context.Url.Action(nameof(UIReportsController.StoreReports), "UIReports", new { storeId = store.Id }), "Payments",
                    "reports");
            }

            if (canViewPaymentRequests)
            {
                AddPage(results, "Payment Requests",
                    context.Url.Action(nameof(UIPaymentRequestController.GetPaymentRequests), "UIPaymentRequest", new { storeId = store.Id }), "Payments",
                    "payment requests");
            }

            if (canViewPullPayments)
            {
                AddPage(results, "Pull Payments", context.Url.Action("PullPayments", "UIStorePullPayments", new { storeId = store.Id }), "Payments", ["pull payments", "pull"]);
            }

            if (canViewPayouts)
            {
                AddPage(results, "Payouts", context.Url.Action("Payouts", "UIStorePullPayments", new { storeId = store.Id }), "Payments", "payouts");
            }
        }

        if (canModifyServer)
        {
            AddPage(results, "Server Settings", context.Url.Action(nameof(UIServerController.Policies), "UIServer"), "Server", "server settings policies");
            AddPage(results, "Users", context.Url.Action(nameof(UIServerController.ListUsers), "UIServer"), "Server", "server settings users");
            AddPage(results, "Roles", context.Url.Action(nameof(UIServerController.ListRoles), "UIServer"), "Server", "server settings roles");
            AddPage(results, "Services", context.Url.Action(nameof(UIServerController.Services), "UIServer"), "Server", "server settings services");
            AddPage(results, "Branding", context.Url.Action(nameof(UIServerController.Branding), "UIServer"), "Server", "server settings branding");
            AddPage(results, "Translations", context.Url.Action(nameof(UIServerController.ListDictionaries), "UIServer"), "Server", "server settings translations");
            AddPage(results, "Maintenance", context.Url.Action(nameof(UIServerController.Maintenance), "UIServer"), "Server", "server settings maintenance");
            AddPage(results, "Logs", context.Url.Action(nameof(UIServerController.LogsView), "UIServer"), "Server", "server settings logs");
            AddPage(results, "Files", context.Url.Action(nameof(UIServerController.Files), "UIServer"), "Server", "server settings files storage");

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
                ["2fa security"]);
            AddPage(results, "API Keys", context.Url.Action(nameof(UIManageController.APIKeys), "UIManage"), "Account", ["Api Keys"]);
            AddPage(results, "Notification Settings", context.Url.Action(nameof(UIManageController.NotificationSettings), "UIManage"), "Account", ["Notifications"]);
            AddPage(results, "Login Codes", context.Url.Action(nameof(UIManageController.LoginCodes), "UIManage"), "Account", ["Login", "Codes", "Login codes"]);
        }

        if (canViewNotifications)
        {
            AddPage(results, "Notifications", context.Url.Action(nameof(UINotificationsController.Index), "UINotifications"), "Account", ["Notifications"]);
        }

        AddPage(results, "Home", context.Url.Action(nameof(UIHomeController.Index), "UIHome"), "General", ["Dashboard", "Overview"]);
        AddPage(results, "Stores", context.Url.Action(nameof(UIUserStoresController.ListStores), "UIUserStores"), "General", ["Stores"]);
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
