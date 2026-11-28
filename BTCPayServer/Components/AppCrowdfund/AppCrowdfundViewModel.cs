using System;
using System.Collections.Generic;

namespace BTCPayServer.Components.AppCrowdfund;

public class AppCrowdfundViewModel
{
    public string Id { get; set; }
    public string AppType { get; set; }
    public string Name { get; set; }
    public string DataUrl { get; set; }
    public bool InitialRendering { get; set; }

    public string Tagline { get; set; }
    public string ManageUrl { get; set; }
    public string PublicUrl { get; set; }
    public string ReportUrl { get; set; }

    // Campaign type / status
    public bool Recurring { get; set; }
    public string RecurrenceLabel { get; set; }  // e.g. "monthly"
    public string PeriodNoun { get; set; }        // e.g. "month"
    public bool Ended { get; set; }
    public bool Enabled { get; set; }
    public bool Started { get; set; }
    public bool GoalReached { get; set; }
    public bool HasTarget { get; set; }

    // Funding progress
    public string Currency { get; set; }
    public decimal? ProgressPercentage { get; set; }
    public int Contributions { get; set; }
    public string CurrentAmountFormatted { get; set; }
    public string CurrentAmountValue { get; set; }
    public string TargetAmountFormatted { get; set; }
    public string RemainingFormatted { get; set; }
    public string LargestFormatted { get; set; }

    // Timing
    public int? DaysLeft { get; set; }
    public int? RenewsInDays { get; set; }
    public DateTime? EndDate { get; set; }

    public List<PerkStat> Perks { get; set; } = new();
    public List<Contribution> RecentContributions { get; set; } = new();

    public class PerkStat
    {
        public string Title { get; set; }
        public string PriceFormatted { get; set; }
        public int Count { get; set; }
    }

    public class Contribution
    {
        public string AmountFormatted { get; set; }
        public DateTimeOffset Date { get; set; }
    }
}
