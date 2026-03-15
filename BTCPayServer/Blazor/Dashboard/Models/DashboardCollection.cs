#nullable enable
using System.Collections.Generic;

namespace BTCPayServer.Blazor.Dashboard.Models;

public class DashboardCollection
{
    public string? ActiveDashboardId { get; set; }
    public List<DashboardDefinition> Dashboards { get; set; } = new();
}
