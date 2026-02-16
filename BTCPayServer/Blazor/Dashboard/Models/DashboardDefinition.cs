#nullable enable
using System;
using System.Collections.Generic;

namespace BTCPayServer.Blazor.Dashboard.Models;

public enum DashboardScope
{
    Server,
    Store,
    User
}

public class DashboardDefinition
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string Name { get; set; } = "Dashboard";
    public string? Description { get; set; }
    public DashboardScope Scope { get; set; }
    public bool IsDefault { get; set; }
    public string? SourceTemplateId { get; set; }
    public List<WidgetPlacement> Widgets { get; set; } = new();
}
