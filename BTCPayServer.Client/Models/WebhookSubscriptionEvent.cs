using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models;

public class WebhookSubscriptionEvent : StoreWebhookEvent
{
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

    // Subscription phases carried by subscriber-related webhook events
    [JsonConverter(typeof(StringEnumConverter))]
    public enum SubscriptionPhase
    {
        Normal,
        Expired,
        Grace,
        Trial
    }

    public class NewSubscriberEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public NewSubscriberEvent()
        {
        }

        public NewSubscriberEvent(string storeId) : base(WebhookEventType.NewSubscriber, storeId)
        {
        }
    }

    public class SubscriberCreditedEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public SubscriberCreditedEvent()
        {
        }

        public SubscriberCreditedEvent(string storeId) : base(WebhookEventType.SubscriberCredited, storeId)
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

        public SubscriberChargedEvent(string storeId) : base(WebhookEventType.SubscriberCharged, storeId)
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

        public SubscriberActivatedEvent(string storeId) : base(WebhookEventType.SubscriberActivated, storeId)
        {
        }
    }


    public class SubscriberPhaseChangedEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public SubscriberPhaseChangedEvent()
        {
        }

        public SubscriberPhaseChangedEvent(string storeId) : base(WebhookEventType.SubscriberPhaseChanged, storeId)
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

        public SubscriberDisabledEvent(string storeId) : base(WebhookEventType.SubscriberDisabled, storeId)
        {
        }
    }

    public class PaymentReminderEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public PaymentReminderEvent()
        {
        }

        public PaymentReminderEvent(string storeId) : base(WebhookEventType.PaymentReminder, storeId)
        {
        }
    }

    public class PlanStartedEvent : WebhookSubscriptionEvent.SubscriberEvent
    {
        public PlanStartedEvent()
        {
        }

        public PlanStartedEvent(string storeId) : base(WebhookEventType.PlanStarted, storeId)
        {
        }

        public bool AutoRenew { get; set; }
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
