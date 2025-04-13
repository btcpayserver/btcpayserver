#nullable enable
using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models;

public abstract class CrowdfundBaseData : AppBaseData
{
    public string? Title { get; set; }
    public bool? Enabled { get; set; }
    public bool? EnforceTargetAmount { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? StartDate { get; set; }
    public string? TargetCurrency { get; set; }
    public string? Description { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? EndDate { get; set; }
    public decimal? TargetAmount { get; set; }
    public string? MainImageUrl { get; set; }
    public string? NotificationUrl { get; set; }
    public string? Tagline { get; set; }
    public bool? DisqusEnabled { get; set; }
    public string? DisqusShortname { get; set; }
    public bool? SoundsEnabled { get; set; }
    public bool? AnimationsEnabled { get; set; }
    public int? ResetEveryAmount { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public CrowdfundResetEvery? ResetEvery { get; set; }
    public bool? DisplayPerksValue { get; set; }
    public bool? DisplayPerksRanking { get; set; }
    public bool? SortPerksByPopularity { get; set; }
    public string[]? Sounds { get; set; }
    public string[]? AnimationColors { get; set; }
    public string? HtmlLang { get; set; }
    public string? HtmlMetaTags { get; set; }
    public string? FormId { get; set; }
}

public class CrowdfundAppData : CrowdfundBaseData
{
    public AppItem[]? Perks { get; set; }
}

public class CrowdfundAppRequest : CrowdfundBaseData, IAppRequest
{
    public string? PerksTemplate { get; set; }
}

public enum CrowdfundResetEvery
{
    Never,
    Hour,
    Day,
    Month,
    Year
}
