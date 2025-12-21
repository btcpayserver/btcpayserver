#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Events;
using BTCPayServer.Plugins.Webhooks;
using Newtonsoft.Json.Linq;


namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriberWebhookProvider : WebhookTriggerProvider<SubscriptionEvent.SubscriberEvent>
{
    protected override async Task<JObject> GetEmailModel(WebhookTriggerContext<SubscriptionEvent.SubscriberEvent> webhookTriggerContext)
    {
        var evt = webhookTriggerContext.Event;
        var model = await base.GetEmailModel(webhookTriggerContext);
        model["Plan"] = new JObject()
        {
            ["Id"] = evt.Subscriber.PlanId,
            ["Name"] = evt.Subscriber.Plan.Name,
        };
        model["Offering"] = new JObject()
        {
            ["Name"] = evt.Subscriber.Offering.App.Name,
            ["Id"] = evt.Subscriber.Offering.Id,
            ["AppId"] = evt.Subscriber.Offering.AppId,
            ["Metadata"] = evt.Subscriber.Offering.Metadata,
        };
        model["Subscriber"] = new JObject()
        {
            ["Phase"] = evt.Subscriber.Phase.ToString(),
            // TODO: When the subscriber can customize the email, also check it!
            ["Email"] = evt.Subscriber.Customer.Email.Get()
        };
        model["Customer"] = new JObject()
        {
            ["ExternalRef"] = evt.Subscriber.Customer.ExternalRef ?? "",
            ["Name"] = evt.Subscriber.Customer.Name,
            ["Metadata"] = evt.Subscriber.Customer.Metadata
        };
        return model;
    }

    protected override StoreWebhookEvent GetWebhookEvent(SubscriptionEvent.SubscriberEvent evt)
    {
        if (evt is null) throw new ArgumentNullException(nameof(evt));
        var sub = evt.Subscriber;
        var storeId = sub.Customer.StoreId;
        var model = Mapper.MapToSubscriberModel(sub);

        switch (evt)
        {
            case SubscriptionEvent.NewSubscriber:
                return new WebhookSubscriptionEvent.NewSubscriberEvent(storeId)
                {
                    Subscriber = model
                };

            case SubscriptionEvent.SubscriberCredited credited:
                return new WebhookSubscriptionEvent.SubscriberCreditedEvent(storeId)
                {
                    Subscriber = model,
                    Total = credited.Total,
                    Amount = credited.Amount,
                    Currency = credited.Currency
                };

            case SubscriptionEvent.SubscriberDebited charged:
                return new WebhookSubscriptionEvent.SubscriberChargedEvent(storeId)
                {
                    Subscriber = model,
                    Total = charged.Total,
                    Amount = charged.Amount,
                    Currency = charged.Currency
                };

            case SubscriptionEvent.SubscriberActivated:
                return new WebhookSubscriptionEvent.SubscriberActivatedEvent(storeId)
                {
                    Subscriber = model
                };

            case SubscriptionEvent.SubscriberPhaseChanged phaseChanged:
                return new WebhookSubscriptionEvent.SubscriberPhaseChangedEvent(storeId)
                {
                    Subscriber = model,
                    PreviousPhase = Mapper.Map(phaseChanged.PreviousPhase),
                    CurrentPhase = Mapper.Map(sub.Phase)
                };

            case SubscriptionEvent.SubscriberDisabled:
                return new WebhookSubscriptionEvent.SubscriberDisabledEvent(storeId)
                {
                    Subscriber = model
                };

            case SubscriptionEvent.PaymentReminder:
                return new WebhookSubscriptionEvent.PaymentReminderEvent(storeId)
                {
                    Subscriber = model
                };

            case SubscriptionEvent.PlanStarted started:
                return new WebhookSubscriptionEvent.PlanStartedEvent(storeId)
                {
                    Subscriber = model,
                    AutoRenew = started.AutoRenew
                };
            case SubscriptionEvent.NeedUpgrade upgrade:
                return new WebhookSubscriptionEvent.NeedUpgradeEvent(storeId)
                {
                    Subscriber = model
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(evt), evt.GetType(), "Unsupported subscription event type");
        }
    }
}
