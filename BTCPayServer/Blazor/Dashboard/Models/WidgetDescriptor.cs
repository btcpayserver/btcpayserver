#nullable enable
using System;

namespace BTCPayServer.Blazor.Dashboard.Models;

public enum WidgetScope
{
    Server,     // Only on server-scoped dashboards, requires admin
    Store,      // Needs a store context, loads store-specific data
    User,       // User-level, no store needed
    Universal   // Works in any dashboard scope
}

public class WidgetDescriptor
{
    public string Type { get; set; } = string.Empty;
    public string Name { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public string Category { get; set; } = string.Empty;
    public Type ComponentType { get; set; } = null!;
    public int MinColumnSize { get; set; } = 2;
    public int MaxColumnSize { get; set; } = 12;
    public int DefaultColumnSize { get; set; } = 6;
    public WidgetScope Scope { get; set; } = WidgetScope.Store;
    public string[] RequiredPermissions { get; set; } = Array.Empty<string>();
    public string[] RequiredStoreFeatures { get; set; } = Array.Empty<string>();
    public bool AllowMultiple { get; set; } = true;
    public string? IconCssClass { get; set; }
}
