#nullable enable
using System.Collections.Generic;
using BTCPayServer.Blazor.Dashboard.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Blazor.Dashboard;

public class DefaultUserDashboardTemplate : IDashboardTemplateProvider
{
    public const int CurrentVersion = 1;

    public string Name => "Default Personal Dashboard";
    public DashboardScope Scope => DashboardScope.User;

    public DashboardDefinition GetTemplate(DashboardTemplateContext context)
    {
        var widgets = new List<WidgetPlacement>();
        var order = 0;

        // Personal dashboards still have store context from the URL,
        // so include the most useful store widgets alongside utility widgets.

        widgets.Add(new WidgetPlacement
        {
            WidgetType = "StoreNumbers",
            ColumnSize = 6,
            Order = order++,
            Config = JObject.FromObject(new { TimeframeDays = 7 })
        });

        widgets.Add(new WidgetPlacement
        {
            WidgetType = "RecentInvoices",
            ColumnSize = 6,
            Order = order++,
            Config = JObject.FromObject(new { Limit = 5 })
        });

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

        // Personal utility widgets
        widgets.Add(new WidgetPlacement
        {
            WidgetType = "Todo",
            ColumnSize = 4,
            Order = order++,
            Config = JObject.FromObject(new { Title = "My Tasks" })
        });

        widgets.Add(new WidgetPlacement
        {
            WidgetType = "Notes",
            ColumnSize = 4,
            Order = order++,
            Config = JObject.FromObject(new { Title = "Notes", Content = "" })
        });

        return new DashboardDefinition
        {
            Name = "My Dashboard",
            IsDefault = true,
            Scope = DashboardScope.User,
            TemplateVersion = CurrentVersion,
            Widgets = widgets
        };
    }
}
