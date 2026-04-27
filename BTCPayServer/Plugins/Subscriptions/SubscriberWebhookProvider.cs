#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Events;
using BTCPayServer.Plugins.Webhooks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;


namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriberWebhookProvider(LinkGenerator linkGenerator) : WebhookTriggerProvider<SubscriptionEvent.SubscriberEvent>
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
            ["Metadata"] = JObject.Parse(evt.Subscriber.Offering.Metadata),
        };
        model["Subscriber"] = new JObject()
        {
            ["Phase"] = evt.Subscriber.Phase.ToString(),
            // TODO: When the subscriber can customize the email, also check it!
            ["Email"] = evt.Subscriber.Customer.Email.Get(),
            ["Metadata"] = JObject.Parse(evt.Subscriber.Metadata)
        };
        model["Customer"] = new JObject()
        {
            ["ExternalRef"] = evt.Subscriber.Customer.ExternalRef ?? "",
            ["Name"] = evt.Subscriber.Customer.Name,
            ["Metadata"] = JObject.Parse(evt.Subscriber.Customer.Metadata)
        };
        if (evt is SubscriptionEvent.CreditRefunded refunded)
        {
            var pullPaymentUrl = linkGenerator.GetUriByAction(nameof(UIPullPaymentController.ViewPullPayment), "UIPullPayment",
                new { pullPaymentId = refunded.PullPaymentId }, refunded.RequestBaseUrl.Scheme, new HostString(refunded.RequestBaseUrl.Host.Host));

            model["Refund"] = new JObject()
            {
                ["Amount"] = refunded.Amount,
                ["Currency"] = refunded.Currency,
                ["ClaimUrl"] = pullPaymentUrl
            };
        }
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

            case SubscriptionEvent.SubscriberDisabled disabled:
                return new WebhookSubscriptionEvent.SubscriberDisabledEvent(storeId)
                {
                    Subscriber = model,
                    Reason = Map(disabled.Reason),
                    SuspensionReason = disabled.SuspensionReason
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
            case SubscriptionEvent.CreditRefunded refunded:
                var pullPaymentUrl = linkGenerator.GetUriByAction(nameof(UIPullPaymentController.ViewPullPayment), "UIPullPayment",
                    new { pullPaymentId = refunded.PullPaymentId }, refunded.RequestBaseUrl.Scheme, new HostString(refunded.RequestBaseUrl.Host.Host));

                return new WebhookSubscriptionEvent.CreditRefundedEvent(storeId)
                {
                    Subscriber = model,
                    Amount = refunded.Amount,
                    Currency = refunded.Currency,
                    PullPaymentUrl = pullPaymentUrl
                };
            default:
                throw new ArgumentOutOfRangeException(nameof(evt), evt.GetType(), "Unsupported subscription event type");
        }
    }

    private WebhookSubscriptionEvent.SubscriberDisabledEvent.DisabledReason Map(SubscriptionEvent.DisabledReason disabledReason)
        => disabledReason switch
        {
            SubscriptionEvent.DisabledReason.Expired => WebhookSubscriptionEvent.SubscriberDisabledEvent.DisabledReason.Expired,
            SubscriptionEvent.DisabledReason.Suspension => WebhookSubscriptionEvent.SubscriberDisabledEvent.DisabledReason.Suspension,
            _ => throw new ArgumentOutOfRangeException(nameof(disabledReason), disabledReason, null)
        };
}
