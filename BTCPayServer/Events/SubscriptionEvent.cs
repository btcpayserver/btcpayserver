#nullable enable
using BTCPayServer.Data;

namespace BTCPayServer.Events;

public class SubscriptionEvent
{
    public class MemberEvent : SubscriptionEvent
    {
        public MemberEvent(SubscriptionMemberData member)
        {
            Member = member;
            PlanAdditionalData = Member.Plan.GetBlob();
        }

        public SubscriptionPlanData.BTCPayAdditionalData? PlanAdditionalData { get; }

        public SubscriptionMemberData Member { get; }
    }

    public class MemberActivated(SubscriptionMemberData member) : MemberEvent(member)
    {
        public override string ToString() => $"Member {Member.CustomerId} activated";
    }

    public class MemberPhaseChanged(SubscriptionMemberData member, SubscriptionMemberData.PhaseTypes previousPhase) : MemberEvent(member)
    {
        public SubscriptionMemberData.PhaseTypes PreviousPhase { get; set; } = previousPhase;
        public override string ToString() => $"Member {Member.CustomerId} changed phase from {PreviousPhase} to {Member.Phase}";
    }

    public class MemberDisabled(SubscriptionMemberData member) : MemberEvent(member)
    {
        public override string ToString() => $"Member {Member.CustomerId} disabled";
    }
}
