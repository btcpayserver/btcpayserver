using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Views.UIStoreMembership;

public class MembershipViewModel
{
    public class PlanViewModel
    {
        public SubscriptionPlanData Data { get; set; }
        public int UserCount { get; set; }
    }

    public MembershipSection Section { get; set; }
    public List<PlanViewModel> Plans { get; set; } = new();
}
