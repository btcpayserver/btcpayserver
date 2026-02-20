#nullable enable
using System.Collections.Generic;
using BTCPayServer.Blazor.Dashboard.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Blazor.Dashboard;

public class DefaultStoreDashboardTemplate : IDashboardTemplateProvider
{
    /// <summary>
    /// Bump this whenever the default layout changes so that auto-materialized
    /// dashboards get refreshed on next load.
    /// </summary>
    public const int CurrentVersion = 1;

    public string Name => "Default Store Dashboard";
    public DashboardScope Scope => DashboardScope.Store;

    public DashboardDefinition GetTemplate(DashboardTemplateContext context)
    {
        // Mirrors the original MVC Dashboard.cshtml layout exactly.
        // The MVC dashboard uses flexbox rows:
        //   Row 1 (wallet-balances): WalletBalance (8 cols)
        //   Row 2 (secondary):       RecentTransactions (8) + StoreNumbers (4) — side by side
        //   Row 3 (tertiary):        RecentInvoices (12)
        //   Row 4:                   LightningBalance (8) + LightningServices (4)
        //   Per-app:                 AppSales (8) + AppTopItems (4)

        var widgets = new List<WidgetPlacement>();
        var order = 0;

        // Row 1: Setup guide (4 cols) + Wallet balance (8 cols) side by side
        widgets.Add(new WidgetPlacement
        {
            WidgetType = "SetupGuide",
            ColumnSize = 4 ,
            Order = order++
        });

        if (context.WalletEnabled)
        {
            widgets.Add(new WidgetPlacement
            {
                WidgetType = "WalletBalance",
                ColumnSize = 8,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode, Period = "Week" })
            });
        }

        // Row 2: Recent transactions (8 cols) + Store numbers (4 cols) — side by side
        if (context.WalletEnabled)
        {
            widgets.Add(new WidgetPlacement
            {
                WidgetType = "RecentTransactions",
                ColumnSize = 8,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode, Limit = 5 })
            });
        }

        widgets.Add(new WidgetPlacement
        {
            WidgetType = "StoreNumbers",
            ColumnSize = 4,
            Order = order++,
            Config = JObject.FromObject(new { TimeframeDays = 7 })
        });

        // Row 3: Recent invoices (full width)
        widgets.Add(new WidgetPlacement
        {
            WidgetType = "RecentInvoices",
            ColumnSize = 12,
            Order = order++,
            Config = JObject.FromObject(new { Limit = 5 })
        });

        // Row 4: Lightning balance (8 cols) + Lightning services (4 cols)
        if (context.LightningEnabled)
        {
            widgets.Add(new WidgetPlacement
            {
                WidgetType = "LightningBalance",
                ColumnSize = 8,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode })
            });

            widgets.Add(new WidgetPlacement
            {
                WidgetType = "LightningServices",
                ColumnSize = 4,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode })
            });
        }

        // Per-app rows: App sales (8 cols) + App top items (4 cols)
        foreach (var app in context.Apps)
        {
            widgets.Add(new WidgetPlacement
            {
                WidgetType = "AppSales",
                ColumnSize = 8,
                Order = order++,
                Config = JObject.FromObject(new { AppId = app.Id })
            });

            widgets.Add(new WidgetPlacement
            {
                WidgetType = "AppTopItems",
                ColumnSize = 4,
                Order = order++,
                Config = JObject.FromObject(new { AppId = app.Id })
            });
        }

        return new DashboardDefinition
        {
            Name = "Default",
            IsDefault = true,
            Scope = DashboardScope.Store,
            TemplateVersion = CurrentVersion,
            Widgets = widgets
        };
    }
}
