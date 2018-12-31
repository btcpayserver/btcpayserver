using System;
using System.ComponentModel.DataAnnotations;

namespace BTCPayServer.Models.AppViewModels
{
    public class UpdateCrowdfundViewModel
    {
        [Required]
        [MaxLength(30)]
        public string Title { get; set; }
        
        [MaxLength(50)]
        public string Tagline { get; set; }
        
        [Required]
        public string Description { get; set; }
        public string MainImageUrl { get; set; }
        
        public string NotificationUrl { get; set; }
        
        [Required]
        
        [Display(Name = "Enabled, Allow crowdfund to be publicly visible( still visible to you)")]
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
        
        [Display(Name = "Disqus Shortname")]
        public string DisqusShortname { get; set; }
        
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }

        [Required]
        [MaxLength(5)]
        [Display(Name = "The primary currency used for targets and stats")]
        public string TargetCurrency { get; set; } = "BTC";
        
        [Display(Name = "Set a Target amount ")]
        public decimal? TargetAmount { get; set; }
        
        
        [Display(Name = "Do not allow additional contributions after target has been reached")]
        public bool EnforceTargetAmount { get; set; }

        [Display(Name = "Contribution Perks Template")]
        public string PerksTemplate { get; set; }
        
        [MaxLength(500)]
        [Display(Name = "Custom bootstrap CSS file")]
        public string CustomCSSLink { get; set; }

        public string EmbeddedCSS { get; set; }
        
    }
}
