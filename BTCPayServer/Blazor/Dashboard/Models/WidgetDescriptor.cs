#nullable enable
using System;
using System.Collections.Generic;

namespace BTCPayServer.Blazor.Dashboard.Models;

public enum WidgetScope
{
    Server,     // Only on server-scoped dashboards, requires admin
    Store,      // Needs a store context, loads store-specific data
    User,       // User-level, no store needed
    Universal   // Works in any dashboard scope
}

public enum ConfigFieldType
{
    Text,
    Textarea,
    Number,
    Select,
    Checkbox,
    Hidden
}

public class ConfigFieldSchema
{
    public string Label { get; set; } = string.Empty;
    public ConfigFieldType FieldType { get; set; } = ConfigFieldType.Text;
    /// <summary>For Select fields: list of (value, displayLabel) pairs.</summary>
    public List<(string Value, string Label)> Options { get; set; } = new();
    public int? Min { get; set; }
    public int? Max { get; set; }
    public int? Rows { get; set; }
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
    public int MinRowSpan { get; set; } = 1;
    public int MaxRowSpan { get; set; } = 4;
    public int DefaultRowSpan { get; set; } = 2;
    public WidgetScope Scope { get; set; } = WidgetScope.Store;
    public string[] RequiredPermissions { get; set; } = Array.Empty<string>();
    public string[] RequiredStoreFeatures { get; set; } = Array.Empty<string>();
    public bool AllowMultiple { get; set; } = true;
    public bool RequiresConfiguration { get; set; }
    /// <summary>When true, the widget remains interactive (not readonly) even when the dashboard is not in edit mode.</summary>
    public bool AlwaysInteractive { get; set; }
    public string? IconCssClass { get; set; }
    public string? CssClass { get; set; }
    /// <summary>Per-property rendering hints for the config panel. Key = property name.</summary>
    public Dictionary<string, ConfigFieldSchema> ConfigSchema { get; set; } = new();
}
