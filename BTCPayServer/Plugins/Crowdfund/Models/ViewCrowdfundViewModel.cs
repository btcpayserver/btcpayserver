using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Client.Models;
using BTCPayServer.Models;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Plugins.Crowdfund.Models
{
    public class ViewCrowdfundViewModel
    {
        public string HubPath { get; set; }
        public string StoreId { get; set; }
        public string AppId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string HtmlLang { get; set; }
        public string HtmlMetaTags{ get; set; }
        public string MainImageUrl { get; set; }
        public string StoreName { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public string TargetCurrency { get; set; }
        public decimal? TargetAmount { get; set; }
        public bool EnforceTargetAmount { get; set; }

        public CrowdfundInfo Info { get; set; }
        public string Tagline { get; set; }
        public StoreBrandingViewModel StoreBranding { get; set; }
        public AppItem[] Perks { get; set; }
        public bool SimpleDisplay { get; set; }
        public bool DisqusEnabled { get; set; }
        public bool SoundsEnabled { get; set; }
        public string DisqusShortname { get; set; }
        public bool AnimationsEnabled { get; set; }
        public string[] AnimationColors { get; set; }
        public string[] Sounds { get; set; }
        public int ResetEveryAmount { get; set; }
        public bool NeverReset { get; set; }
        public string FormUrl { get; set; }
        public Dictionary<string, int> PerkCount { get; set; }

        public CurrencyData CurrencyData { get; set; }

        public class CrowdfundInfo
        {
            public class PaymentStat
            {
                public string Label { get; set; }
                public decimal Percent { get; set; }
                public bool IsLightning { get; set; }
            }
            public int TotalContributors { get; set; }
            public decimal CurrentPendingAmount { get; set; }
            public decimal CurrentAmount { get; set; }
            public decimal? ProgressPercentage { get; set; }
            public decimal? PendingProgressPercentage { get; set; }
            public DateTime LastUpdated { get; set; }
            public Dictionary<string, PaymentStat> PaymentStats { get; set; }
            public DateTime? LastResetDate { get; set; }
            public DateTime? NextResetDate { get; set; }
        }


        public bool Started => !StartDate.HasValue || DateTime.UtcNow > StartDate;

        public bool Ended => EndDate.HasValue && DateTime.UtcNow > EndDate;
        public bool DisplayPerksRanking { get; set; }
        public bool DisplayPerksValue { get; set; }
        public bool Enabled { get; set; }
        public string ResetEvery { get; set; }
        public Dictionary<string, decimal> PerkValue { get; set; }
    }

    public class ContributeToCrowdfund
    {
        public ViewCrowdfundViewModel ViewCrowdfundViewModel { get; set; }
        [Required] public decimal? Amount { get; set; }
        public string Email { get; set; }
        public string ChoiceKey { get; set; }
        public bool RedirectToCheckout { get; set; }
        public string RedirectUrl { get; set; }
    }
}
