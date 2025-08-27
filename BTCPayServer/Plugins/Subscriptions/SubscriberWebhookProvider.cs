#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data.Subscriptions;
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
            ["Name"] = evt.Subscriber.Plan.Name ?? "",
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
            ["Name"] = evt.Subscriber.Customer.Name ?? "",
            ["Metadata"] = evt.Subscriber.Customer.Metadata
        };
        return model;
    }

    protected override StoreWebhookEvent GetWebhookEvent(SubscriptionEvent.SubscriberEvent evt)
    {
        if (evt is null) throw new ArgumentNullException(nameof(evt));
        var sub = evt.Subscriber;
        var storeId = sub.Customer.StoreId;
        var model = MapToSubscriberModel(sub);

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
                    PreviousPhase = MapPhase(phaseChanged.PreviousPhase),
                    CurrentPhase = MapPhase(sub.Phase)
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

        static WebhookSubscriptionEvent.SubscriptionPhase MapPhase(SubscriberData.PhaseTypes p) =>
            p switch
            {
                SubscriberData.PhaseTypes.Normal => WebhookSubscriptionEvent.SubscriptionPhase.Normal,
                SubscriberData.PhaseTypes.Expired => WebhookSubscriptionEvent.SubscriptionPhase.Expired,
                SubscriberData.PhaseTypes.Grace => WebhookSubscriptionEvent.SubscriptionPhase.Grace,
                SubscriberData.PhaseTypes.Trial => WebhookSubscriptionEvent.SubscriptionPhase.Trial,
                _ => WebhookSubscriptionEvent.SubscriptionPhase.Expired
            };
    }

    private static SubscriberModel MapToSubscriberModel(SubscriberData sub)
    {
        if (sub is null) throw new ArgumentNullException(nameof(sub));

        var customer = sub.Customer;
        var offering = sub.Offering;
        var plan = sub.Plan;

        return new SubscriberModel
        {
            Customer = new CustomerModel
            {
                StoreId = customer.StoreId,
                Id = sub.CustomerId,
                ExternalId = customer.ExternalRef
            },
            Offer = new OfferingModel
            {
                Id = sub.OfferingId,
                AppName = offering.App?.Name,
                AppId = offering.AppId,
                SuccessRedirectUrl = offering.SuccessRedirectUrl
            },
            Plan = new SubscriptionPlanModel
            {
                Id = sub.PlanId,
                Name = plan.Name,
                Status = plan.Status switch
                {
                    PlanData.PlanStatus.Active => SubscriptionPlanModel.PlanStatus.Active,
                    PlanData.PlanStatus.Retired => SubscriptionPlanModel.PlanStatus.Retired,
                    _ => SubscriptionPlanModel.PlanStatus.Retired
                },
                Price = plan.Price,
                Currency = plan.Currency,
                RecurringType = plan.RecurringType switch
                {
                    PlanData.RecurringInterval.Monthly => SubscriptionPlanModel.RecurringInterval.Monthly,
                    PlanData.RecurringInterval.Quarterly => SubscriptionPlanModel.RecurringInterval.Quarterly,
                    PlanData.RecurringInterval.Yearly => SubscriptionPlanModel.RecurringInterval.Yearly,
                    _ => SubscriptionPlanModel.RecurringInterval.Lifetime
                },
                GracePeriodDays = plan.GracePeriodDays,
                TrialDays = plan.TrialDays,
                Description = plan.Description,
                MemberCount = plan.MemberCount,
                OptimisticActivation = plan.OptimisticActivation,
                Entitlements = plan.GetEntitlementIds()
            },
            PeriodEnd = sub.PeriodEnd,
            TrialEnd = sub.TrialEnd,
            GracePeriodEnd = sub.GracePeriodEnd,
            IsActive = sub.IsActive,
            IsSuspended = sub.IsSuspended,
            SuspensionReason = sub.SuspensionReason
        };
    }
}
