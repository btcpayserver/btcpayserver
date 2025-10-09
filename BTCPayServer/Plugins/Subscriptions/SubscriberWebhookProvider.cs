#nullable  enable
using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Events;
using BTCPayServer.HostedServices.Webhooks;
using Microsoft.Extensions.Logging;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;


namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriberWebhookProvider(EventAggregator eventAggregator, ILogger<SubscriberWebhookProvider> logger, WebhookSender webhookSender) : WebhookProvider<SubscriptionEvent.SubscriberEvent>(eventAggregator, logger, webhookSender)
{

    public override bool SupportsCustomerEmail { get; } = true;

    public override Dictionary<string, string> GetSupportedWebhookTypes()
    {
        return new Dictionary<string, string>
        {
            { WebhookSubscriptionEvent.NewSubscriber, "Subscription - New subscriber" },
            { WebhookSubscriptionEvent.SubscriberCredited, "Subscription - Subscriber credited" },
            { WebhookSubscriptionEvent.SubscriberCharged, "Subscription - Subscriber charged" },
            { WebhookSubscriptionEvent.SubscriberActivated, "Subscription - Subscriber activated" },
            { WebhookSubscriptionEvent.SubscriberPhaseChanged, "Subscription - Subscriber phase changed" },
            { WebhookSubscriptionEvent.SubscriberDisabled, "Subscription - Subscriber disabled" },
            { WebhookSubscriptionEvent.PaymentReminder, "Subscription - Payment reminder" },
            { WebhookSubscriptionEvent.PlanStarted, "Subscription - Plan started" }
        };
    }

    public override WebhookEvent CreateTestEvent(string type, params object[] args)
    {
        var storeId = "def";
        var member = CreateTestMember();

        if (type == WebhookSubscriptionEvent.NewSubscriber)
        {
            return new WebhookSubscriptionEvent.NewSubscriberEvent(storeId)
            {
                Subscriber = member
            };
        }
        if (type == WebhookSubscriptionEvent.SubscriberCredited)
        {
            return new WebhookSubscriptionEvent.SubscriberCreditedEvent(storeId)
            {
                Subscriber = member,
                Amount = 10.0m,
                Total = 100.0m,
                Currency = "USD"
            };
        }
        if (type == WebhookSubscriptionEvent.SubscriberCharged)
        {
            return new WebhookSubscriptionEvent.SubscriberChargedEvent(storeId)
            {
                Subscriber = member,
                Amount = 10.0m,
                Total = 90.0m,
                Currency = "USD"
            };
        }
        if (type == WebhookSubscriptionEvent.SubscriberActivated)
        {
            return new WebhookSubscriptionEvent.SubscriberActivatedEvent(storeId)
            {
                Subscriber = member
            };
        }
        if (type == WebhookSubscriptionEvent.SubscriberPhaseChanged)
        {
            return new WebhookSubscriptionEvent.SubscriberPhaseChangedEvent(storeId)
            {
                Subscriber = member,
                PreviousPhase = WebhookSubscriptionEvent.SubscriptionPhase.Trial,
                CurrentPhase = WebhookSubscriptionEvent.SubscriptionPhase.Normal
            };
        }
        if (type == WebhookSubscriptionEvent.SubscriberDisabled)
        {
            return new WebhookSubscriptionEvent.SubscriberDisabledEvent(storeId)
            {
                Subscriber = member
            };
        }
        if (type == WebhookSubscriptionEvent.PaymentReminder)
        {
            return new WebhookSubscriptionEvent.PaymentReminderEvent(storeId)
            {
                Subscriber = member
            };
        }
        if (type == WebhookSubscriptionEvent.PlanStarted)
        {
            return new WebhookSubscriptionEvent.PlanStartedEvent(storeId)
            {
                Subscriber = member,
                AutoRenew = false
            };
        }

        // Fallback: return a disabled event if an unknown type is requested
        return new WebhookSubscriptionEvent.SubscriberDisabledEvent(storeId)
        {
            Subscriber = member
        };
    }

    protected override void SubscribeToEvents()
    {
        SubscribeAny<WebhookSubscriptionEvent.SubscriberEvent>();
    }

    class SubscriberWebhookDeliveryRequest(SubscriptionEvent.SubscriberEvent evt, string webhookId, WebhookEvent webhookEvent, WebhookDeliveryData delivery, WebhookBlob webhookBlob) : WebhookSender.WebhookDeliveryRequest(webhookId, webhookEvent, delivery, webhookBlob)
    {
        public override Task<SendEmailRequest?> Interpolate(SendEmailRequest req, UIStoresController.StoreEmailRule storeEmailRule)
        {
            if (storeEmailRule.CustomerEmail &&
                MailboxAddressValidator.TryParse(evt.Subscriber.Customer.Email.Get() ?? "", out var bmb))
            {
                req.Email ??= string.Empty;
                req.Email += $",{bmb}";
            }

            req.Subject = Interpolate(req.Subject);
            req.Body = Interpolate(req.Body);
            return Task.FromResult(req)!;
        }

        static readonly Regex _interpolationRegex = new Regex(@"\{([^}]+)\}", RegexOptions.Compiled);
        private string Interpolate(string str)
        {
            var  map = new Dictionary<string,string>(StringComparer.OrdinalIgnoreCase)
            {
                ["StoreId"]  = evt.Subscriber.Customer.StoreId,
                ["Plan.Id"] = evt.Subscriber.PlanId,
                ["Plan.Name"] = evt.Subscriber.Plan.Name ?? "",
                ["Offering.Name"] = evt.Subscriber.Offering.App.Name,
                ["Offering.Id"] = evt.Subscriber.Offering.Id,
                ["Offering.AppId"] = evt.Subscriber.Offering.AppId,
                ["Subscriber.Phase"] = evt.Subscriber.Phase.ToString(),
                ["Customer.ExternalRef"] = evt.Subscriber.Customer.ExternalRef ?? "",
                ["Customer.Name"] = evt.Subscriber.Customer.Name ?? "",
            };
            return _interpolationRegex.Replace(str, match =>
            {
                var key = match.Groups[1].Value;
                if (map.TryGetValue(key, out var value))
                {
                    return value;
                }
                return match.Value;
            });
        }
    }

    protected override WebhookSender.WebhookDeliveryRequest CreateDeliveryRequest(SubscriptionEvent.SubscriberEvent evt, WebhookData webhook)
    {
        var webhookEvent = GetWebhookEvent(evt)!;
        // ReSharper disable once ConditionIsAlwaysTrueOrFalseAccordingToNullableAPIContract
        // Hack in WebhookProvider means it can be null...
        if (webhook is null)
            return new SubscriberWebhookDeliveryRequest(evt, null!, webhookEvent,
                null!, null!);
        var webhookBlob = webhook.GetBlob();
        webhookEvent.WebhookId = webhook.Id;
        webhookEvent.IsRedelivery = false;
        var delivery = WebhookExtensions.NewWebhookDelivery(webhook.Id);
        webhookEvent.DeliveryId = delivery.Id;
        webhookEvent.OriginalDeliveryId = delivery.Id;
        webhookEvent.Timestamp = delivery.Timestamp;
        return new SubscriberWebhookDeliveryRequest(evt, webhook.Id, webhookEvent,
            delivery, webhookBlob);
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

            case SubscriptionEvent.SubscriberCharged charged:
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
                    _ => SubscriptionPlanModel.RecurringInterval.Forever
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

    private SubscriberModel CreateTestMember()
    {
        return new SubscriberModel()
        {
            Customer = new CustomerModel() { Id = "test-customer-id", StoreId = "def", ExternalId = "test-external-id" },
            Plan = new SubscriptionPlanModel() { Id = "test-plan-id", Entitlements = ["test-item-id"]},
            PeriodEnd = DateTimeOffset.UtcNow.AddDays(30),
            TrialEnd = DateTimeOffset.UtcNow.AddDays(7),
            GracePeriodEnd = DateTimeOffset.UtcNow.AddDays(37),
            IsActive = true,
        };
    }
}
