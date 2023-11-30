using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public enum PosViewType
    {
        Static,
        Cart,
        Light,
        Print
    }

    public class CreateAppRequest
    {
        public string AppName { get; set; }
        public string AppType { get; set; }
    }

    public class CreatePointOfSaleAppRequest : CreateAppRequest
    {
        public string Currency { get; set; } = null;
        public string Title { get; set; } = null;
        public string Description { get; set; } = null;
        public string Template { get; set; } = null;
        [JsonConverter(typeof(StringEnumConverter))]
        public PosViewType DefaultView { get; set; }
        public bool ShowCustomAmount { get; set; } = false;
        public bool ShowDiscount { get; set; } = true;
        public bool ShowSearch { get; set; } = true;
        public bool ShowCategories { get; set; } = true;
        public bool EnableTips { get; set; } = true;
        public string CustomAmountPayButtonText { get; set; } = null;
        public string FixedAmountPayButtonText { get; set; } = null;
        public string TipText { get; set; } = null;
        public string CustomCSSLink { get; set; } = null;
        public string NotificationUrl { get; set; } = null;
        public string RedirectUrl { get; set; } = null;
        public bool? RedirectAutomatically { get; set; } = null;
        public bool? RequiresRefundEmail { get; set; } = null;
        public bool? Archived { get; set; } = null;
        public string FormId { get; set; } = null;
        public string EmbeddedCSS { get; set; } = null;
    }

    public enum CrowdfundResetEvery
    {
        Never,
        Hour,
        Day,
        Month,
        Year
    }

    public class CreateCrowdfundAppRequest : CreateAppRequest
    {
        public string Title { get; set; } = null;
        public bool? Enabled { get; set; } = null;
        public bool? EnforceTargetAmount { get; set; } = null;
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? StartDate { get; set; } = null;
        public string TargetCurrency { get; set; } = null;
        public string Description { get; set; } = null;
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? EndDate { get; set; } = null;
        public decimal? TargetAmount { get; set; } = null;
        public string CustomCSSLink { get; set; } = null;
        public string MainImageUrl { get; set; } = null;
        public string EmbeddedCSS { get; set; } = null;
        public string NotificationUrl { get; set; } = null;
        public string Tagline { get; set; } = null;
        public string PerksTemplate { get; set; } = null;
        public bool? SoundsEnabled { get; set; } = null;
        public string DisqusShortname { get; set; } = null;
        public bool? AnimationsEnabled { get; set; } = null;
        public int? ResetEveryAmount { get; set; } = null;
        [JsonConverter(typeof(StringEnumConverter))]
        public CrowdfundResetEvery ResetEvery { get; set; } = CrowdfundResetEvery.Never;
        public bool? DisplayPerksValue { get; set; } = null;
        public bool? DisplayPerksRanking { get; set; } = null;
        public bool? SortPerksByPopularity { get; set; } = null;
        public bool? Archived { get; set; } = null;
        public string[] Sounds { get; set; } = null;
        public string[] AnimationColors { get; set; } = null;
    }
}
