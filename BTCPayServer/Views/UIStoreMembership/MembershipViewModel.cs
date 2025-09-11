using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Views.UIStoreMembership;

public class MembershipViewModel
{
    public class PlanViewModel
    {
        public SubscriptionPlanData Data { get; set; }
    }

    public class MemberViewModel
    {
        public SubscriptionMemberData Data { get; set; }
    }

    public MembershipSection Section { get; set; }
    public List<PlanViewModel> Plans { get; set; } = new();
    public List<MemberViewModel> Members { get; set; } = new();
    public SubscriptionStatsData Stats { get; set; }
    public string Currency { get; set; }
}
