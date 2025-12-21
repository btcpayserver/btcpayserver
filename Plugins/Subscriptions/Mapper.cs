using System;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Data.Subscriptions;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Subscriptions;

public static class Mapper
{
    public static SubscriptionPhase Map(SubscriberData.PhaseTypes p) =>
        p switch
        {
            SubscriberData.PhaseTypes.Normal => SubscriptionPhase.Normal,
            SubscriberData.PhaseTypes.Expired => SubscriptionPhase.Expired,
            SubscriberData.PhaseTypes.Grace => SubscriptionPhase.Grace,
            SubscriberData.PhaseTypes.Trial => SubscriptionPhase.Trial,
            _ => SubscriptionPhase.Expired
        };
    public static SubscriberData.PhaseTypes Map(SubscriptionPhase subscriptionPhase)
        => subscriptionPhase switch
        {
            SubscriptionPhase.Normal => SubscriberData.PhaseTypes.Normal,
            SubscriptionPhase.Expired => SubscriberData.PhaseTypes.Expired,
            SubscriptionPhase.Grace => SubscriberData.PhaseTypes.Grace,
            SubscriptionPhase.Trial => SubscriberData.PhaseTypes.Trial,
            _ => SubscriberData.PhaseTypes.Expired
        };
    public static OfferingPlanModel.RecurringInterval Map(PlanData plan)
        => plan.RecurringType switch
        {
            PlanData.RecurringInterval.Monthly => OfferingPlanModel.RecurringInterval.Monthly,
            PlanData.RecurringInterval.Quarterly => OfferingPlanModel.RecurringInterval.Quarterly,
            PlanData.RecurringInterval.Yearly => OfferingPlanModel.RecurringInterval.Yearly,
            _ => OfferingPlanModel.RecurringInterval.Lifetime
        };

    public static PlanData.RecurringInterval Map(OfferingPlanModel.RecurringInterval recurringInterval)
        => recurringInterval switch
        {
            OfferingPlanModel.RecurringInterval.Monthly => PlanData.RecurringInterval.Monthly,
            OfferingPlanModel.RecurringInterval.Quarterly => PlanData.RecurringInterval.Quarterly,
            OfferingPlanModel.RecurringInterval.Yearly => PlanData.RecurringInterval.Yearly,
            _ => PlanData.RecurringInterval.Lifetime
        };

    public static SubscriberModel MapToSubscriberModel(SubscriberData sub)
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
                ExternalId = customer.ExternalRef,
                Identities = customer.CustomerIdentities is {} v ? new JObject(v.Select(i => new JProperty(i.Type, i.Value))) : null,
                Metadata = JObject.Parse(customer.Metadata)
            },
            Created = sub.CreatedAt,
            Offering = MapOffering(offering),
            Plan = MapPlan(plan),
            PeriodEnd = sub.PeriodEnd,
            TrialEnd = sub.TrialEnd,
            GracePeriodEnd = sub.GracePeriodEnd,
            IsActive = sub.IsActive,
            IsSuspended = sub.IsSuspended,
            Phase = Map(sub.Phase),
            SuspensionReason = sub.SuspensionReason,
            AutoRenew = sub.AutoRenew,
            Metadata = JObject.Parse(sub.Metadata),
            ProcessingInvoiceId = sub.ProcessingInvoiceId,
            NextPlan = MapPlan(sub.NextPlan)
        };
    }

    public static OfferingPlanModel MapPlan(PlanData plan)
    {
        return new OfferingPlanModel
        {
            Id = plan.Id,
            Name = plan.Name,
            Status = plan.Status switch
            {
                PlanData.PlanStatus.Active => OfferingPlanModel.PlanStatus.Active,
                PlanData.PlanStatus.Retired => OfferingPlanModel.PlanStatus.Retired,
                _ => OfferingPlanModel.PlanStatus.Retired
            },
            Price = plan.Price,
            Currency = plan.Currency,
            RecurringType = Map(plan),
            GracePeriodDays = plan.GracePeriodDays,
            TrialDays = plan.TrialDays,
            Description = plan.Description,
            MemberCount = plan.MemberCount,
            OptimisticActivation = plan.OptimisticActivation,
            Renewable = plan.Renewable,
            Features = plan.GetFeatureIds(),
            Metadata = JObject.Parse(plan.Metadata),
        };
    }

    public static OfferingModel MapOffering(OfferingData offering)
    => new()
    {
        Id = offering.Id,
        StoreId = offering.App.StoreDataId,
        AppName = offering.App.Name,
        AppId = offering.AppId,
        SuccessRedirectUrl = offering.SuccessRedirectUrl,
        Plans = offering?.Plans is {} plans ? plans.Select(MapPlan).ToList() : null,
        Features = offering?.Features is {} features ? features.Select(MapFeature).ToList() : null,
        Metadata = JObject.Parse(offering.Metadata)
    };

    private static FeatureModel MapFeature(FeatureData arg)
        => new()
        {
            Id = arg.CustomId,
            Description = arg.Description
        };

    public static PlanCheckoutModel MapPlanCheckout(PlanCheckoutData checkout)
        => new()
        {
            Id = checkout.Id,
            InvoiceId = checkout.InvoiceId,
            Subscriber = checkout.Subscriber is null ? null : MapToSubscriberModel(checkout.Subscriber),
            Plan = MapPlan(checkout.Plan),
            SuccessRedirectUrl = checkout.SuccessRedirectUrl,
            Expiration = checkout.Expiration,
            RedirectUrl = checkout.GetRedirectUrl(),
            BaseUrl = checkout.BaseUrl.ToString(),
            InvoiceMetadata = JObject.Parse(checkout.InvoiceMetadata),
            Metadata = JObject.Parse(checkout.Metadata),
            NewSubscriber = checkout.NewSubscriber,
            IsTrial = checkout.IsTrial,
            Created = checkout.CreatedAt,
            PlanStarted = checkout.PlanStarted,
            NewSubscriberMetadata = JObject.Parse(checkout.NewSubscriberMetadata),
            RefundAmount = checkout.RefundAmount,
            CreditedByInvoice = checkout.CreditedByInvoice,
            OnPayBehavior = MapOnPay(checkout.OnPay),
            IsExpired = checkout.IsExpired,
            CreditPurchase = checkout.CreditPurchase,
            Url = checkout.BaseUrl.GetUrl($"/plan-checkout/{checkout.Id}")
        };

    private static OnPayBehavior MapOnPay(PlanCheckoutData.OnPayBehavior behavior)
        => behavior switch
        {
            PlanCheckoutData.OnPayBehavior.HardMigration => OnPayBehavior.HardMigration,
            PlanCheckoutData.OnPayBehavior.SoftMigration => OnPayBehavior.SoftMigration,
            _ => throw new NotSupportedException(nameof(behavior))
        };
    public static PlanCheckoutData.OnPayBehavior Map(OnPayBehavior onPayBehavior)
    => onPayBehavior switch
    {
        OnPayBehavior.HardMigration => PlanCheckoutData.OnPayBehavior.HardMigration,
        OnPayBehavior.SoftMigration => PlanCheckoutData.OnPayBehavior.SoftMigration,
        _ => throw new NotSupportedException(nameof(onPayBehavior))
    };

    public static PortalSessionModel MapPortalSession(PortalSessionData session)
        => new()
        {
            Id = session.Id,
            BaseUrl = session.BaseUrl.ToString(),
            Subscriber = MapToSubscriberModel(session.Subscriber),
            Expiration = session.Expiration,
            IsExpired = session.IsExpired,
            Url = session.BaseUrl.GetUrl($"subscriber-portal/{session.Id}")
        };


}
