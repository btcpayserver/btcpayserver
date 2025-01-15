using System;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using System.Globalization;
using System.Linq;

namespace BTCPayServer.Services.Apps
{
    public class CrowdfundSettings
    {
        public CrowdfundSettings()
        {
        }

        public string Title { get; set; }
        public string Description { get; set; }
        public string HtmlLang { get; set; }
        public string HtmlMetaTags{ get; set; }
        public bool Enabled { get; set; } = true;
        public DateTime? StartDate { get; set; }
        public DateTime? EndDate { get; set; }
        public string TargetCurrency { get; set; }
        decimal? _TargetAmount;
        public decimal? TargetAmount
        {
            get
            {
                // In the past, target amount might have been 0, creating exception
                if (_TargetAmount is decimal v && v == 0.0m)
                    return null;
                return _TargetAmount;
            }
            set
            {
                _TargetAmount = value;
            }
        }

        public bool EnforceTargetAmount { get; set; }
        [JsonConverter(typeof(UnresolvedUriJsonConverter))]
        public UnresolvedUri MainImageUrl { get; set; }
        public string NotificationUrl { get; set; }
        public string Tagline { get; set; }
        public string PerksTemplate { get; set; }
        public bool DisqusEnabled { get; set; }
        public bool SoundsEnabled { get; set; }
        public string DisqusShortname { get; set; }
        public bool AnimationsEnabled { get; set; }
        public int ResetEveryAmount { get; set; } = 1;
        public CrowdfundResetEvery ResetEvery { get; set; } = CrowdfundResetEvery.Never;
        public bool DisplayPerksRanking { get; set; }
        public bool DisplayPerksValue { get; set; }
        public bool SortPerksByPopularity { get; set; }
        public string FormId { get; set; }
        public string[] AnimationColors { get; set; } =
        [
            "#FF6138", "#FFBE53", "#2980B9", "#282741"
        ];

        public string[] Sounds { get; set; } =
        [
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/dominating.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/doublekill.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/doublekill2.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/eagleeye.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/firstblood.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/firstblood2.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/firstblood3.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/godlike.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/hattrick.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/headhunter.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/headshot.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/headshot2.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/headshot3.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/holyshit.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/killingspree.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/ludicrouskill.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/megakill.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/monsterkill.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/multikill.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/ownage.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/payback.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/rampage.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/teamkiller.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/triplekill.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/ultrakill.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/unstoppable.wav",
            "https://github.com/ClaudiuHKS/AdvancedQuakeSounds/tree/master/sound/AQS/whickedsick.wav"
        ];
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
