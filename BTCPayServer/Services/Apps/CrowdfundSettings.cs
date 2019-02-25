using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Apps
{
    public class CrowdfundSettings
    {
        public string Title { get; set; }
        public string Description { get; set; }
        public bool Enabled { get; set; } = false;

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public string TargetCurrency { get; set; }
        public decimal? TargetAmount { get; set; }

        public bool EnforceTargetAmount { get; set; }
        public string CustomCSSLink { get; set; }
        public string MainImageUrl { get; set; }
        public string NotificationUrl { get; set; }
        public string Tagline { get; set; }
        public string EmbeddedCSS { get; set; }
        public string PerksTemplate { get; set; }
        public bool DisqusEnabled { get; set; } = false;
        public bool SoundsEnabled { get; set; } = true;
        public string DisqusShortname { get; set; }
        public bool AnimationsEnabled { get; set; } = true;
        public int ResetEveryAmount { get; set; } = 1;
        public CrowdfundResetEvery ResetEvery { get; set; } = CrowdfundResetEvery.Never;
        [Obsolete("Use AppData.TagAllInvoices instead")]
        public bool UseAllStoreInvoices { get; set; }
        public bool DisplayPerksRanking { get; set; }
        public bool SortPerksByPopularity { get; set; }
    }
    public enum CrowdfundResetEvery
    {
        Never,
        Hour,
        Day,
        Month,
        Year
    }
}
