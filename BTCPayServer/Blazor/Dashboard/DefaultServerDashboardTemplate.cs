#nullable enable
using System.Collections.Generic;
using BTCPayServer.Blazor.Dashboard.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Blazor.Dashboard;

public class DefaultServerDashboardTemplate : IDashboardTemplateProvider
{
    public string Name => "Default Server Dashboard";
    public DashboardScope Scope => DashboardScope.Server;

    public DashboardDefinition GetTemplate(DashboardTemplateContext context)
    {
        var widgets = new List<WidgetPlacement>();
        var order = 0;

        // Server dashboard provides an overview using store-context widgets
        // (store context comes from the URL, so these still work).

        widgets.Add(new WidgetPlacement
        {
            WidgetType = "StoreNumbers",
            ColumnSize = 6,
            Order = order++,
            Config = JObject.FromObject(new { TimeframeDays = 30 })
        });

        widgets.Add(new WidgetPlacement
        {
            WidgetType = "RecentInvoices",
            ColumnSize = 6,
            Order = order++,
            Config = JObject.FromObject(new { Limit = 10 })
        });

        if (context.WalletEnabled)
        {
            widgets.Add(new WidgetPlacement
            {
                WidgetType = "WalletBalance",
                ColumnSize = 12,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode, Period = "Month" })
            });
        }

        if (context.LightningEnabled)
        {
            widgets.Add(new WidgetPlacement
            {
                WidgetType = "LightningBalance",
                ColumnSize = 6,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode })
            });

            widgets.Add(new WidgetPlacement
            {
                WidgetType = "LightningServices",
                ColumnSize = 6,
                Order = order++,
                Config = JObject.FromObject(new { CryptoCode = context.CryptoCode })
            });
        }

        return new DashboardDefinition
        {
            Name = "Server Overview",
            IsDefault = true,
            Scope = DashboardScope.Server,
            Widgets = widgets
        };
    }
}
