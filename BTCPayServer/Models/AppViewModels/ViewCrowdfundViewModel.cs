using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;

namespace BTCPayServer.Models.AppViewModels
{
    public class ViewCrowdfundViewModel
    {
        public string StatusMessage{ get; set; }
        public string StoreId { get; set; }
        public string AppId { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string MainImageUrl { get; set; }
        public string EmbeddedCSS { get; set; }
        public string CustomCSSLink { get; set; }
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        public string TargetCurrency { get; set; }
        public decimal? TargetAmount { get; set; }
        public bool EnforceTargetAmount { get; set; }

        public CrowdfundInfo Info { get; set; }
        public string Tagline { get; set; }
        public ViewPointOfSaleViewModel.Item[] Perks { get; set; }
        public bool DisqusEnabled { get; set; }
        public bool SoundsEnabled { get; set; }
        public string DisqusShortname { get; set; }
        public bool AnimationsEnabled { get; set; }
        public int ResetEveryAmount { get; set; }
        public string ResetEvery { get; set; }

        public Dictionary<string, int> PerkCount { get; set; }

        public CurrencyData CurrencyData { get; set; }
        
        public class CrowdfundInfo
        {
            public int TotalContributors { get; set; }
            public decimal CurrentPendingAmount { get; set; }
            public decimal CurrentAmount { get; set; }
            public decimal? ProgressPercentage { get; set; }
            public decimal? PendingProgressPercentage { get; set; }
            public DateTime LastUpdated { get; set; }
            public Dictionary<string, decimal> PaymentStats { get; set; }
            public Dictionary<string, decimal> PendingPaymentStats { get; set; }
            public DateTime? LastResetDate { get; set; }
            public DateTime? NextResetDate { get; set; }
        }

        public bool Started => !StartDate.HasValue || DateTime.Now.ToUniversalTime() > StartDate;

        public bool Ended => !EndDate.HasValue || DateTime.Now.ToUniversalTime() > EndDate;
        public bool DisplayPerksRanking { get; set; }
    }

    public class ContributeToCrowdfund
    {
        public ViewCrowdfundViewModel ViewCrowdfundViewModel { get; set; }
        [Required] public decimal Amount { get; set; }
        public string Email { get; set; }
        public string ChoiceKey { get; set; }
        public bool RedirectToCheckout { get; set; }
        public string RedirectUrl { get; set; }
    }
}
