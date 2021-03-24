using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Services.Apps;
using BTCPayServer.Validation;

namespace BTCPayServer.Models.AppViewModels
{
    public class UpdateCrowdfundViewModel
    {
        public string StoreId { get; set; }
        public string StoreName { get; set; }

        [Required]
        [MaxLength(30)]
        public string Title { get; set; }

        [MaxLength(50)]
        public string Tagline { get; set; }

        [Required]
        public string Description { get; set; }

        [Display(Name = "Featured Image")]
        public string MainImageUrl { get; set; }

        [Display(Name = "Callback Notification Url")]
        [Uri]
        public string NotificationUrl { get; set; }

        [Required]
        [Display(Name = "Allow crowdfund to be publicly visible (still visible to you)")]
        public bool Enabled { get; set; } = false;

        [Required]
        [Display(Name = "Enable background animations on new payments")]
        public bool AnimationsEnabled { get; set; } = true;

        [Required]
        [Display(Name = "Enable sounds on new payments")]
        public bool SoundsEnabled { get; set; } = true;

        [Required]
        [Display(Name = "Enable Disqus Comments")]
        public bool DisqusEnabled { get; set; } = true;

        [Display(Name = "Disqus Shortname")] public string DisqusShortname { get; set; }

        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        [Required]
        [MaxLength(5)]
        [Display(Name = "Primary currency used for targets and stats. (e.g. BTC, LTC, USD, etc.)")]
        public string TargetCurrency { get; set; } = "BTC";

        [Display(Name = "Set a Target amount ")]
        [Range(0, double.PositiveInfinity)]
        public decimal? TargetAmount { get; set; }


        public IEnumerable<string> ResetEveryValues = Enum.GetNames(typeof(CrowdfundResetEvery));

        [Display(Name = "Reset goal every")]
        public string ResetEvery { get; set; } = nameof(CrowdfundResetEvery.Never);

        public int ResetEveryAmount { get; set; } = 1;


        [Display(Name = "Do not allow additional contributions after target has been reached")]
        public bool EnforceTargetAmount { get; set; }

        [Display(Name = "Contribution Perks Template")]
        public string PerksTemplate { get; set; }

        [MaxLength(500)]
        [Display(Name = "Custom bootstrap CSS file")]
        public string CustomCSSLink { get; set; }

        [Display(Name = "Custom CSS Code")]
        public string EmbeddedCSS { get; set; }

        [Display(Name = "Count all invoices created on the store as part of the crowdfunding goal")]
        public bool UseAllStoreInvoices { get; set; }

        public string AppId { get; set; }
        public string SearchTerm { get; set; }

        [Display(Name = "Sort contribution perks by popularity")]
        public bool SortPerksByPopularity { get; set; }
        [Display(Name = "Display contribution ranking")]
        public bool DisplayPerksRanking { get; set; }


        [Display(Name = "Sounds to play when a payment is made. One sound per line")]
        public string Sounds { get; set; }
        [Display(Name = "Colors to rotate between with animation when a payment is made. First color is the default background. One color per line. Can be any valid css color value.")]
        public string AnimationColors { get; set; }

        // NOTE: Improve validation if needed
        public bool ModelWithMinimumData => Description != null && Title != null && TargetCurrency != null;
    }
}
