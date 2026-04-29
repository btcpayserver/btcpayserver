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
        return new DashboardDefinition
        {
            Name = "My Dashboard",
            IsDefault = true,
            Scope = DashboardScope.User,
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
                        Title = "Personal notes",
                        Content = "Your personal scratchpad — visible only to you, regardless of which store you're viewing."
                    })
                }
            }
        };
    }
}
