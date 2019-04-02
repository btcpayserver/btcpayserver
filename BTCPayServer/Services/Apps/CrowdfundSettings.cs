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
        public string[] AnimationColors { get; set; } = new string[]
        {
            "#FF6138", "#FFBE53", "#2980B9", "#282741"
        };

        public string[] Sounds { get; set; } = new string[]
        {
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/dominating.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/doublekill.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/doublekill2.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/eagleeye.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/firstblood.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/firstblood2.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/firstblood3.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/flawless.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/godlike.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/hattrick.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/headhunter.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/headshot.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/headshot2.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/headshot3.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/holyshit.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/killingspree.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/knife.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/knife2.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/knife3.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/ludicrouskill.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/megakill.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/monsterkill.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/multikill.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/nade.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/ownage.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/payback.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/prepare.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/prepare2.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/prepare3.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/prepare4.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/rampage.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/suicide.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/suicide2.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/suicide3.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/suicide4.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/teamkiller.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/triplekill.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/ultrakill.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/unstoppable.wav",
            "//github.com/ClaudiuHKS/AdvancedQuakeSounds/raw/master/sound/QuakeSounds/whickedsick.wav"
        };

        public string NotificationEmail { get; set; }
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
