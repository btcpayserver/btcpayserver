using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;

namespace BTCPayServer.Views.UIStoreMembership;

public class SubscriptionsViewModel
{
    public class PlanViewModel
    {
        public PlanData Data { get; set; }
    }

    public class MemberViewModel
    {
        public SubscriberData Data { get; set; }
    }

    public SubscriptionSection Section { get; set; }
    public List<PlanViewModel> Plans { get; set; } = new();
    public List<MemberViewModel> Subscribers { get; set; } = new();
    public string Currency { get; set; }
}
