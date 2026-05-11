#nullable enable
using System.Collections.Generic;
using BTCPayServer.Plugins.Dashboard.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Dashboard;

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
        return new DashboardDefinition
        {
            Name = "Default",
            IsDefault = true,
            Scope = DashboardScope.Store,
            TemplateVersion = CurrentVersion,
            Widgets = new List<WidgetPlacement>
            {
                new()
                {
                    WidgetType = "Notes",
                    ColumnSize = 6,
                    Order = 0,
                    Config = JObject.FromObject(new
                    {
                        Title = "Welcome to your dashboard",
                        Content = "This dashboard is fully customizable. Use the Add Widget button to drop new widgets onto the grid, then drag and resize them to fit how you work."
                    })
                }
            }
        };
    }
}
