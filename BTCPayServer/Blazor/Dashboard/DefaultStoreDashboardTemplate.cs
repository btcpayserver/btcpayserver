#nullable enable
using System.Collections.Generic;
using BTCPayServer.Blazor.Dashboard.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Blazor.Dashboard;

public class DefaultStoreDashboardTemplate : IDashboardTemplateProvider
{
    public string Name => "Default Store Dashboard";
    public DashboardScope Scope => DashboardScope.Store;

    public DashboardDefinition GetTemplate(DashboardTemplateContext context)
    {
        var widgets = new List<WidgetPlacement>();
        var order = 0;

        if (context.WalletEnabled)
        {
            widgets.Add(new WidgetPlacement
            {
                WidgetType = "WalletBalance",
                ColumnSize = 12,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode, Period = "Week" })
            });
        }

        widgets.Add(new WidgetPlacement
        {
            WidgetType = "StoreNumbers",
            ColumnSize = context.WalletEnabled ? 6 : 12,
            Order = order++,
            Config = JObject.FromObject(new { TimeframeDays = 7 })
        });

        if (context.WalletEnabled)
        {
            widgets.Add(new WidgetPlacement
            {
                WidgetType = "RecentTransactions",
                ColumnSize = 6,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode, Limit = 5 })
            });
        }

        widgets.Add(new WidgetPlacement
        {
            WidgetType = "RecentInvoices",
            ColumnSize = 12,
            Order = order++,
            Config = JObject.FromObject(new { Limit = 5 })
        });

        if (context.LightningEnabled)
        {
            widgets.Add(new WidgetPlacement
            {
                WidgetType = "LightningBalance",
                ColumnSize = 12,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode })
            });

            widgets.Add(new WidgetPlacement
            {
                WidgetType = "LightningServices",
                ColumnSize = 12,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode })
            });
        }

        foreach (var app in context.Apps)
        {
            widgets.Add(new WidgetPlacement
            {
                WidgetType = "AppSales",
                ColumnSize = 6,
                Order = order++,
                Config = JObject.FromObject(new { AppId = app.Id })
            });

            widgets.Add(new WidgetPlacement
            {
                WidgetType = "AppTopItems",
                ColumnSize = 6,
                Order = order++,
                Config = JObject.FromObject(new { AppId = app.Id })
            });
        }

        return new DashboardDefinition
        {
            Name = "Default",
            IsDefault = true,
            Scope = DashboardScope.Store,
            Widgets = widgets
        };
    }
}
