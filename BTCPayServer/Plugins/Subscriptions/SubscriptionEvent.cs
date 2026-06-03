#nullable enable
using BTCPayServer.Abstractions;
using BTCPayServer.Data.Subscriptions;

namespace BTCPayServer.Events;

public class SubscriptionEvent
{
    public class SubscriberEvent(SubscriberData subscriber) : SubscriptionEvent
    {
        public SubscriberData Subscriber { get; } = subscriber;
    }

    public class NewSubscriber(SubscriberData subscriber, RequestBaseUrl requestBaseUrl) : SubscriberEvent(subscriber)
    {
        public RequestBaseUrl RequestBaseUrl { get; } = requestBaseUrl;
        public override string ToString() => $"New Subscriber {Subscriber.ToNiceString()}";
    }

    public class SubscriberCredited(SubscriberData subscriber, decimal total, decimal amount, string currency) : SubscriberEvent(subscriber)
    {
        public decimal Total { get; } = total;
        public decimal Amount { get; set; } = amount;
        public string Currency { get; set; } = currency;
        public override string ToString() => $"Subscriber {Subscriber.ToNiceString()} credited (Amount: {Amount} {Currency}, New Total: {Total} {Currency})";
    }
    public class SubscriberDebited(SubscriberData subscriber, decimal total, decimal amount, string currency) : SubscriberEvent(subscriber)
    {
        public decimal Total { get; } = total;
        public decimal Amount { get; set; } = amount;
        public string Currency { get; set; } = currency;
        public override string ToString() => $"Subscriber {Subscriber.ToNiceString()} debited (Amount: {Amount} {Currency}, New Total: {Total} {Currency})";
    }

    public class CreditRefunded(SubscriberData subscriber, decimal amount, string currency, string pullPaymentId, RequestBaseUrl requestBaseUrl) : SubscriberEvent(subscriber)
    {
        public decimal Amount { get; } = amount;
        public string Currency { get; } = currency;
        public string PullPaymentId { get; } = pullPaymentId;
        public RequestBaseUrl RequestBaseUrl { get; } = requestBaseUrl;
        public override string ToString() => $"Subscriber {Subscriber.ToNiceString()} credit refunded (Amount: {Amount} {Currency})";
    }

    public class SubscriberActivated(SubscriberData subscriber) : SubscriberEvent(subscriber)
    {
        public override string ToString() => $"Subscriber {Subscriber.ToNiceString()} activated";
    }

    public class SubscriberPhaseChanged(SubscriberData subscriber, SubscriberData.PhaseTypes previousPhase) : SubscriberEvent(subscriber)
    {
        public SubscriberData.PhaseTypes PreviousPhase { get; set; } = previousPhase;
        public override string ToString() => $"Subscriber {Subscriber.ToNiceString()} changed phase from {PreviousPhase} to {Subscriber.Phase}";
    }

    public enum DisabledReason
    {
        Suspension,
        Expired
    }

    public class SubscriberDisabled(SubscriberData subscriber, DisabledReason reason, string? suspensionReason = null) : SubscriberEvent(subscriber)
    {
        public DisabledReason Reason { get; } = reason;
        public string? SuspensionReason { get; } = reason is DisabledReason.Suspension ? suspensionReason : null;
        public override string ToString() => $"Subscriber {Subscriber.ToNiceString()} disabled";
    }

    public class PaymentReminder(SubscriberData subscriber) : SubscriberEvent(subscriber)
    {
        public override string ToString() => $"Subscriber {Subscriber.ToNiceString()} needs reminder";
    }

    public class PlanUpdated : SubscriptionEvent
    {
        public PlanUpdated(PlanData plan)
        {
            Plan = plan;
            plan.AssertFeaturesLoaded();
        }

        public PlanData Plan { get; set; }
    }

    public class NeedUpgrade(SubscriberData subscriber) : SubscriberEvent(subscriber)
    {
    }

    public class PlanStarted(SubscriberData subscriber, PlanData previous) : SubscriberEvent(subscriber)
    {
        public PlanData PreviousPlan { get; } = previous;
        public bool AutoRenew { get; set; }
        public override string ToString() => $"Subscriber {Subscriber.ToNiceString()} started plan";
    }
}
