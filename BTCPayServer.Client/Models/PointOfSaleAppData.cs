#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models;

public abstract class PointOfSaleBaseData : AppBaseData
{
    public string? Title { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public PosViewType? DefaultView { get; set; }
    public bool? ShowItems { get; set; }
    public bool? ShowCustomAmount { get; set; }
    public bool? ShowDiscount { get; set; }
    public bool? ShowSearch { get; set; }
    public bool? ShowCategories { get; set; }
    public bool? EnableTips { get; set; }
    public string? Currency { get; set; }
    public string? FixedAmountPayButtonText { get; set; }
    public string? CustomAmountPayButtonText { get; set; }
    public string? TipText { get; set; }
    public string? NotificationUrl { get; set; }
    public string? RedirectUrl { get; set; }
    public string? Description { get; set; }
    public bool? RedirectAutomatically { get; set; }
    public int[]? CustomTipPercentages { get; set; }
    public string? HtmlLang { get; set; }
    public string? HtmlMetaTags { get; set; }
    public string? FormId { get; set; }
}

public class PointOfSaleAppData : PointOfSaleBaseData
{
    public AppItem[]? Items { get; set; }
}

public class PointOfSaleAppRequest : PointOfSaleBaseData, IAppRequest
{
    public string? Template { get; set; }
}

public enum PosViewType
{
    Static,
    Cart,
    Light,
    Print
}
