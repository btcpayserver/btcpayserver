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
    /// <summary>
    /// Version of the template this dashboard was created from.
    /// When a code-default template's version changes, auto-materialized dashboards
    /// with an older version are replaced by the fresh template on next load.
    /// </summary>
    public int TemplateVersion { get; set; }
    /// <summary>
    /// True when this dashboard was auto-materialized from a code default and
    /// has never been manually edited by the user.
    /// </summary>
    public bool AutoMaterialized { get; set; }
    public List<WidgetPlacement> Widgets { get; set; } = new();
}
