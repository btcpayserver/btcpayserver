#nullable enable
using System;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Blazor.Dashboard.Models;

public class WidgetPlacement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WidgetType { get; set; } = string.Empty;
    public int Order { get; set; }
    public int ColumnSize { get; set; } = 6;
    public int RowSpan { get; set; } = 2;
    /// <summary>
    /// Explicit gridstack column offset, or null if not yet placed (auto-flow).
    /// Nullable so that an explicit column 0 placement round-trips correctly through
    /// drag-save-reload (the previous int default treated 0 as "unset").
    /// </summary>
    public int? Offset { get; set; }
    public JObject? Config { get; set; }
}
