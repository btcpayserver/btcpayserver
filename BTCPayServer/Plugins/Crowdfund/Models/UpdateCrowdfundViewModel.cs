using System;
using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using System.Linq;
using BTCPayServer.Services.Apps;
using BTCPayServer.Validation;

namespace BTCPayServer.Plugins.Crowdfund.Models
{
    public class UpdateCrowdfundViewModel
    {
        public string StoreId { get; set; }
        public string StoreName { get; set; }
        public string StoreDefaultCurrency { get; set; }

        [Required]
        [MaxLength(50)]
        [MinLength(1)]
        [Display(Name = "App Name")]
        public string AppName { get; set; }

        [Required]
        [MaxLength(30)]
        [Display(Name = "Display Title")]
        public string Title { get; set; }

        public string Tagline { get; set; }

        [Required]
        public string Description { get; set; }

        [Display(Name = "Featured Image URL")]
        public string MainImageUrl { get; set; }

        [Display(Name = "Callback Notification URL")]
        [Uri]
        public string NotificationUrl { get; set; }

        [Required]
        [Display(Name = "Make Crowdfund Public")]
        public bool Enabled { get; set; }

        [Required]
        [Display(Name = "Enable background animations on new payments")]
        public bool AnimationsEnabled { get; set; } = true;

        [Required]
        [Display(Name = "Enable sounds on new payments")]
        public bool SoundsEnabled { get; set; }

        [Required]
        [Display(Name = "Enable Disqus Comments")]
        public bool DisqusEnabled { get; set; }

        [Display(Name = "Disqus Shortname")]
        public string DisqusShortname { get; set; }

        [Display(Name = "Start date")]
        public DateTime? StartDate { get; set; }

        [Display(Name = "End date")]
        public DateTime? EndDate { get; set; }

        [MaxLength(5)]
        [Display(Name = "Currency")]
        public string TargetCurrency { get; set; }

        [Display(Name = "Target Amount")]
        [Range(0, double.PositiveInfinity)]
        public decimal? TargetAmount { get; set; }

        public IEnumerable<string> ResetEveryValues = Enum.GetNames(typeof(CrowdfundResetEvery))
            .Where(i => i != nameof(CrowdfundResetEvery.Never));

        public bool IsRecurring { get; set; }

        [Display(Name = "Reset goal every")]
        public string ResetEvery { get; set; } = nameof(CrowdfundResetEvery.Never);

        [Display(Name = "Reset goal every")]
        public int ResetEveryAmount { get; set; } = 1;

        [Display(Name = "Do not allow additional contributions after target has been reached")]
        public bool EnforceTargetAmount { get; set; }

        [Display(Name = "Contribution Perks Template")]
        public string PerksTemplate { get; set; }

        [Display(Name = "Count all invoices created on the store as part of the goal")]
        public bool UseAllStoreInvoices { get; set; }

        public string AppId { get; set; }
        public string SearchTerm { get; set; }

        [Display(Name = "Sort contribution perks by popularity")]
        public bool SortPerksByPopularity { get; set; }

        [Display(Name = "Display contribution ranking")]
        public bool DisplayPerksRanking { get; set; }

        [Display(Name = "Display contribution value")]
        public bool DisplayPerksValue { get; set; }

        [Display(Name = "Sounds to play when a payment is made. One sound per line")]
        public string Sounds { get; set; }

        [Display(Name = "Colors to rotate between with animation when a payment is made. One color per line.")]
        public string AnimationColors { get; set; }

        // NOTE: Improve validation if needed
        public bool ModelWithMinimumData => Description != null && Title != null && TargetCurrency != null;


        [Display(Name = "Request contributor data on checkout")]
        public string FormId { get; set; }

        public bool Archived { get; set; }
    }
}
