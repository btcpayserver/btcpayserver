#nullable enable
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;

namespace BTCPayServer.Events;

public class SubscriptionEvent
{
    public class MemberEvent : SubscriptionEvent
    {
        public MemberEvent(SubscriberData member)
        {
            Member = member;
        }
        public SubscriberData Member { get; }
    }

    public class MemberActivated(SubscriberData member) : MemberEvent(member)
    {
        public override string ToString() => $"Member {Member.CustomerId} activated";
    }

    public class MemberPhaseChanged(SubscriberData member, SubscriberData.PhaseTypes previousPhase) : MemberEvent(member)
    {
        public SubscriberData.PhaseTypes PreviousPhase { get; set; } = previousPhase;
        public override string ToString() => $"Member {Member.CustomerId} changed phase from {PreviousPhase} to {Member.Phase}";
    }

    public class MemberDisabled(SubscriberData member) : MemberEvent(member)
    {
        public override string ToString() => $"Member {Member.CustomerId} disabled";
    }
}
