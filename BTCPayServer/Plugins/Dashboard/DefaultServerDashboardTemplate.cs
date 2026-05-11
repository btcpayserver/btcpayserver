#nullable enable
using System.Collections.Generic;
using BTCPayServer.Plugins.Dashboard.Models;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Dashboard;

public class DefaultServerDashboardTemplate : IDashboardTemplateProvider
{
    public const int CurrentVersion = 1;

    public string Name => "Default Server Dashboard";
    public DashboardScope Scope => DashboardScope.Server;

    public DashboardDefinition GetTemplate(DashboardTemplateContext context)
    {
        return new DashboardDefinition
        {
            Name = "Server Overview",
            IsDefault = true,
            Scope = DashboardScope.Server,
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
                        Title = "Admin notes",
                        Content = "Server-wide notes visible only to administrators. Use this space for operational reminders, on-call notes, or links to runbooks."
                    })
                }
            }
        };
    }
}
