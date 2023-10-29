using System;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class AppDataBase
    {
        public string Id { get; set; }
        public string AppType { get; set; }
        public string Name { get; set; }
        public string StoreId { get; set; }
        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public bool? Archived { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset Created { get; set; }
    }

    public class PointOfSaleAppData : AppDataBase
    {
        public string Title { get; set; }
        public string DefaultView { get; set; }
        public bool ShowCustomAmount { get; set; }
        public bool ShowDiscount { get; set; }
        public bool ShowSearch { get; set; }
        public bool ShowCategories { get; set; }
        public bool EnableTips { get; set; }
        public string Currency { get; set; }
        public object Items { get; set; }
        public string FixedAmountPayButtonText { get; set; }
        public string CustomAmountPayButtonText { get; set; }
        public string TipText { get; set; }
        public string CustomCSSLink { get; set; }
        public string NotificationUrl { get; set; }
        public string RedirectUrl { get; set; }
        public string Description { get; set; }
        public string EmbeddedCSS { get; set; }
        public bool? RedirectAutomatically { get; set; }
        public bool? RequiresRefundEmail { get; set; }
    }

    public class CrowdfundAppData : AppDataBase
    {
        public string Title { get; set; }
        public bool Enabled { get; set; }
        public bool EnforceTargetAmount { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? StartDate { get; set; }
        public string TargetCurrency { get; set; }
        public string Description { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? EndDate { get; set; }
        public decimal? TargetAmount { get; set; }
        public string CustomCSSLink { get; set; }
        public string MainImageUrl { get; set; }
        public string EmbeddedCSS { get; set; }
        public string NotificationUrl { get; set; }
        public string Tagline { get; set; }
        public object Perks { get; set; }
        public bool DisqusEnabled { get; set; }
        public string DisqusShortname { get; set; }
        public bool SoundsEnabled { get; set; }
        public bool AnimationsEnabled { get; set; }
        public int ResetEveryAmount { get; set; }
        public string ResetEvery { get; set; }
        public bool DisplayPerksValue { get; set; }
        public bool DisplayPerksRanking { get; set; }
        public bool SortPerksByPopularity { get; set; }
        public string[] Sounds { get; set; }
        public string[] AnimationColors { get; set; }
    }
}
