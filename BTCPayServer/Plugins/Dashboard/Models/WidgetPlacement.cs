#nullable enable
using System;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Dashboard.Models;

public class WidgetPlacement
{
    public string Id { get; set; } = Guid.NewGuid().ToString();
    public string WidgetType { get; set; } = string.Empty;
    /// <summary>
    /// Logical/legacy ordering hint for templates and the auto-flow packer.
    /// For widgets that have been placed via the grid (Row.HasValue), Row is
    /// authoritative; Order is kept around for backwards compatibility with
    /// pre-Row JSON and is mirrored to the gridstack y on save.
    /// </summary>
    public int Order { get; set; }
    public int ColumnSize { get; set; } = 6;
    public int RowSpan { get; set; } = 2;
    /// <summary>
    /// Explicit gridstack column offset (x), or null if not yet placed (auto-flow).
    /// Nullable so that an explicit column 0 placement round-trips correctly through
    /// drag-save-reload (the previous int default treated 0 as "unset").
    /// </summary>
    public int? Offset { get; set; }
    /// <summary>
    /// Explicit gridstack row coordinate (y), or null when the widget should
    /// auto-flow via the packer. Persisting y separately is required so that
    /// real GridStack arrangements with gaps or stacked widgets beside taller
    /// neighbours survive a save-reload cycle — reconstructing y from Order
    /// loses that information.
    /// </summary>
    public int? Row { get; set; }
    public JObject? Config { get; set; }
}
