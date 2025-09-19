using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class WebhookSubscriptionEvent : StoreWebhookEvent
{
    public class MemberEvent : WebhookSubscriptionEvent
    {
        public MemberEvent()
        {
        }

        public MemberEvent(string eventType, string storeId) : base(eventType, storeId)
        {
        }

        public SubscriptionMemberModel Member { get; set; }
    }

    public class MemberActivated : MemberEvent
    {
        public MemberActivated()
        {
            Type = WebhookEventType.SubscriptionMemberActivated;
        }

        public MemberActivated(string storeId, string customerId) : base(WebhookEventType.SubscriptionMemberActivated, storeId)
        {

        }
    }
    public class MemberDisabled : MemberEvent
    {
        public MemberDisabled()
        {
            Type = WebhookEventType.SubscriptionMemberDisabled;
        }

        public MemberDisabled(string storeId, string customerId) : base(WebhookEventType.SubscriptionMemberDisabled, storeId)
        {

        }
    }

    public WebhookSubscriptionEvent()
    {
    }

    public WebhookSubscriptionEvent(string evtType, string storeId)
    {
        if (!evtType.StartsWith("Subscription", StringComparison.InvariantCultureIgnoreCase))
            throw new ArgumentException("Invalid event type", nameof(evtType));
        Type = evtType;
        StoreId = storeId;
    }
}
