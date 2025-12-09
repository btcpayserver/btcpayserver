using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models;

public class WebhookSubscriptionEvent : StoreWebhookEvent
{
    public const string SubscriberCreated = nameof(SubscriberCreated);
    public const string SubscriberCredited = nameof(SubscriberCredited);
    public const string SubscriberCharged = nameof(SubscriberCharged);
    public const string SubscriberActivated = nameof(SubscriberActivated);
    public const string SubscriberPhaseChanged = nameof(SubscriberPhaseChanged);
    public const string SubscriberDisabled = nameof(SubscriberDisabled);
    public const string PaymentReminder = nameof(PaymentReminder);
    public const string PlanStarted = nameof(PlanStarted);
    public const string SubscriberNeedUpgrade = nameof(SubscriberNeedUpgrade);

    public static bool IsSubscriptionTrigger(string trigger)
        => IsSubscriptionType(trigger.Substring(3));
    public static bool IsSubscriptionType(string substring)
    => substring is
        SubscriberCreated or
        SubscriberCredited or
        SubscriberCharged or
        SubscriberActivated or
        SubscriberPhaseChanged or
        SubscriberDisabled or
        PaymentReminder or
        PlanStarted;
    public class SubscriberEvent : WebhookSubscriptionEvent
    {
        public SubscriberEvent()
        {
        }

        public SubscriberEvent(string eventType, string storeId) : base(eventType, storeId)
        {
        }

        public SubscriberModel Subscriber { get; set; }
    }

    public class NewSubscriberEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public NewSubscriberEvent()
        {
        }

        public NewSubscriberEvent(string storeId) : base(SubscriberCreated, storeId)
        {
        }
    }

    public class SubscriberCreditedEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public SubscriberCreditedEvent()
        {
        }

        public SubscriberCreditedEvent(string storeId) : base(SubscriberCredited, storeId)
        {
        }

        public decimal Total { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
    }


    public class SubscriberChargedEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public SubscriberChargedEvent()
        {
        }

        public SubscriberChargedEvent(string storeId) : base(SubscriberCharged, storeId)
        {
        }

        public decimal Total { get; set; }
        public decimal Amount { get; set; }
        public string Currency { get; set; }
    }


    public class SubscriberActivatedEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public SubscriberActivatedEvent()
        {
        }

        public SubscriberActivatedEvent(string storeId) : base(SubscriberActivated, storeId)
        {
        }
    }


    public class SubscriberPhaseChangedEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public SubscriberPhaseChangedEvent()
        {
        }

        public SubscriberPhaseChangedEvent(string storeId) : base(SubscriberPhaseChanged, storeId)
        {
        }

        public SubscriptionPhase PreviousPhase { get; set; }
        public SubscriptionPhase CurrentPhase { get; set; }
    }

    public class SubscriberDisabledEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public SubscriberDisabledEvent()
        {
        }

        public SubscriberDisabledEvent(string storeId) : base(SubscriberDisabled, storeId)
        {
        }
    }

    public class PaymentReminderEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public PaymentReminderEvent()
        {
        }

        public PaymentReminderEvent(string storeId) : base(PaymentReminder, storeId)
        {
        }
    }

    public class PlanStartedEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public PlanStartedEvent()
        {
        }

        public PlanStartedEvent(string storeId) : base(PlanStarted, storeId)
        {
        }

        public bool AutoRenew { get; set; }
    }

    public class NeedUpgradeEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public NeedUpgradeEvent()
        {
        }

        public NeedUpgradeEvent(string storeId) : base(SubscriberNeedUpgrade, storeId)
        {
        }
    }


    public WebhookSubscriptionEvent()
    {
    }

    public WebhookSubscriptionEvent(string evtType, string storeId)
    {
        Type = evtType;
        StoreId = storeId;
    }
}
