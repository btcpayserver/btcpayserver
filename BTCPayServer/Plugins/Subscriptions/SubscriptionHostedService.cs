#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Events;
using BTCPayServer.HostedServices;
using BTCPayServer.JsonConverters;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Dapper;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static BTCPayServer.Data.Subscriptions.SubscriberData;

namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriptionHostedService(
    EventAggregator eventAggregator,
    ApplicationDbContextFactory applicationDbContextFactory,
    SettingsRepository settingsRepository,
    UIInvoiceController invoiceController,
    LinkGenerator linkGenerator,
    Logs logger) : EventHostedServiceBase(eventAggregator, logger), IPeriodicTask
{
    record Poll;

    public record SubscribeRequest(string CheckoutId, RequestBaseUrl RequestBaseUrl, CustomerSelector CustomerSelector);

    public Task Do(CancellationToken cancellationToken)
        => base.RunEvent(new Poll(), cancellationToken);

    protected override void SubscribeToEvents()
    {
        this.Subscribe<Events.InvoiceEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is Events.InvoiceEvent
            {
                EventCode: Events.InvoiceEventCode.Completed or
                Events.InvoiceEventCode.MarkedCompleted or
                Events.InvoiceEventCode.MarkedInvalid or
                Events.InvoiceEventCode.PaidInFull,
                Invoice:
                {
                    Status: InvoiceStatus.Settled or InvoiceStatus.Invalid or InvoiceStatus.Processing
                } settledInvoice
            } &&
            GetCheckoutPlanIdFromInvoice(settledInvoice) is string checkoutId)
        {
            await ProcessSubscriptionPayment(settledInvoice, checkoutId, cancellationToken);
        }
        else if (evt is Poll)
        {
            var from = (await settingsRepository.GetSettingAsync<MembershipServerSettings>("MembershipHostedService"))?.LastUpdate;
            var to = DateTimeOffset.UtcNow;
            await using (var ctx = applicationDbContextFactory.CreateContext())
            {
                await UpdateSubscriptionStates(ctx, new MemberSelector.PassedDate(from, to), cancellationToken);
            }

            await settingsRepository.UpdateSetting(new MembershipServerSettings(to), "MembershipHostedService");
        }
        else if (evt is SubscribeRequest subscribeRequest)
        {
            await using var ctx = applicationDbContextFactory.CreateContext();
            var checkout = await ctx.PlanCheckouts.GetCheckout(subscribeRequest.CheckoutId);
            if (checkout is null)
                throw new InvalidOperationException("Checkout not found");
            var redirectLink =
                checkout.GetRedirectUrl() ??
                checkout.Plan.Offering.SuccessRedirectUrl ??
                linkGenerator.GetUriByAction(
                    action: nameof(UIStoreSubscriptionsController.PlanCheckoutDefaultRedirect),
                    values: new { },
                    controller: "UIStoreSubscriptions",
                    scheme: subscribeRequest.RequestBaseUrl.Scheme,
                    host: subscribeRequest.RequestBaseUrl.Host,
                    pathBase: subscribeRequest.RequestBaseUrl.PathBase) ?? throw new InvalidOperationException("Bug, unable to generate redirect link");
            if (checkout.SuccessRedirectUrl != redirectLink)
            {
                checkout.SuccessRedirectUrl = redirectLink;
                await ctx.SaveChangesAsync(cancellationToken);
            }

            if (checkout.IsTrial)
                await ProcessStartTrial(ctx, checkout, subscribeRequest.CustomerSelector, cancellationToken);
            else
                await CreateInvoiceForCheckout(ctx, checkout, subscribeRequest, cancellationToken);
        }
    }

    public static string GetCheckoutPlanTag(string checkoutId) => $"SUBS#{checkoutId}";
    public static string? GetCheckoutPlanIdFromInvoice(InvoiceEntity invoiceEntiy) => invoiceEntiy.GetInternalTags("SUBS#").FirstOrDefault();

    private async Task CreateInvoiceForCheckout(ApplicationDbContext ctx, PlanCheckoutData checkout, SubscribeRequest subscribeRequest, CancellationToken cancellationToken)
    {
        var invoiceMetadata = JObject.Parse(checkout.InvoiceMetadata);
        invoiceMetadata["planId"] = checkout.PlanId;
        invoiceMetadata["offeringId"] = checkout.Plan.OfferingId;

        var mergedInvoiceMetadata = JObject.Parse(checkout.InvoiceMetadata);
        mergedInvoiceMetadata.Merge(invoiceMetadata);
        invoiceMetadata = mergedInvoiceMetadata;

        var plan = checkout.Plan;
        var request = await invoiceController.CreateInvoiceCoreRaw(new()
            {
                Currency = plan.Currency,
                Amount = plan.Price,
                Checkout = new()
                {
                    RedirectAutomatically = true,
                    RedirectURL = checkout.GetRedirectUrl()
                },
                Metadata = invoiceMetadata
            }, plan.Offering.App.StoreData, subscribeRequest.RequestBaseUrl.ToString(),
            [GetCheckoutPlanTag(checkout.Id)], cancellationToken,
            entity =>
            {
                entity.SubscriberId = checkout.SubscriberId;
                entity.CustomerId = checkout.Subscriber?.CustomerId;
            });
        checkout.InvoiceId = request.Id;
        await ctx.SaveChangesAsync(cancellationToken);
    }


    class MembershipServerSettings
    {
        public MembershipServerSettings()
        {
        }

        public MembershipServerSettings(DateTimeOffset lastUpdate)
        {
            LastUpdate = lastUpdate;
        }

        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset LastUpdate { get; set; }
    }

    abstract record MemberSelector
    {
        public record Single(long SubscriberId) : MemberSelector
        {
            public override IQueryable<SubscriberData> Where(IQueryable<SubscriberData> query)
                => query.Where(m => m.Id == SubscriberId);
        }

        public record PassedDate(DateTimeOffset? From, DateTimeOffset To) : MemberSelector
        {
            public override IQueryable<SubscriberData> Where(IQueryable<SubscriberData> query)
                => From is null
                    ? query.Where(q => (q.PeriodEnd < To || q.GracePeriodEnd < To || q.TrialEnd < To))
                    : query.Where(q =>
                        (q.PeriodEnd >= From && q.PeriodEnd < To) || (q.GracePeriodEnd >= From && q.GracePeriodEnd < To) ||
                        (q.TrialEnd >= From && q.TrialEnd < To));
        }

        public abstract IQueryable<SubscriberData> Where(IQueryable<SubscriberData> query);
    }

    private async Task UpdateSubscriptionStates(ApplicationDbContext ctx, MemberSelector selector, CancellationToken cancellationToken)
    {
        List<SubscriptionEvent> events = new();
        var query = ctx.Subscribers.Include(m => m.Plan).Include(m => m.Customer);
        var members = await selector.Where(query).ToListAsync(cancellationToken);
        foreach (var m in members)
        {
            var newPhase = m.CalculateExpectedPhase(DateTimeOffset.UtcNow);
            var (prevPhase, prevActive) = (m.Phase, m.IsActive);
            if (prevPhase != newPhase)
            {
                m.Phase = newPhase;
                events.Add(new SubscriptionEvent.MemberPhaseChanged(m, prevPhase));
            }

            var newActive = !m.ForceDisabled && newPhase != PhaseTypes.Expired;
            if (prevActive != newActive)
            {
                m.IsActive = newActive;
                if (newActive)
                    events.Add(new SubscriptionEvent.MemberActivated(m));
                else
                    events.Add(new SubscriptionEvent.MemberDisabled(m));
            }
        }

        await ctx.SaveChangesAsync(cancellationToken);
        foreach (var e in events)
        {
            EventAggregator.Publish(e);
        }
    }

    public class OptimisticActivationData
    {
        public const string Key = "optimisticActivation";

        public static OptimisticActivationData? GetAdditionalData(BaseEntityData entity)
            => entity.GetAdditionalData<OptimisticActivationData>(Key);

        [JsonProperty]
        [JsonConverter(typeof(DateTimeMilliJsonConverter))]
        public DateTimeOffset? PeriodEnd { get; set; }

        [JsonProperty]
        [JsonConverter(typeof(DateTimeMilliJsonConverter))]
        public DateTimeOffset? TrialEnd { get; set; }

        [JsonProperty]
        [JsonConverter(typeof(DateTimeMilliJsonConverter))]
        public DateTimeOffset? GracePeriodEnd { get; set; }
    }

    public async Task ProceedToSubscribe(string checkoutId, RequestBaseUrl requestBaseUrl, CustomerSelector selector, CancellationToken cancellationToken)
    {
        await RunEvent(new SubscribeRequest(checkoutId, requestBaseUrl, selector), cancellationToken);
    }

    private async Task ProcessStartTrial(ApplicationDbContext ctx, PlanCheckoutData checkout, CustomerSelector customerSelector, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;

        var sub = checkout.Subscriber;
        if (sub is null && !checkout.NewSubscriber)
            throw new InvalidOperationException("Bug: Subscriber is null and not a new subscriber");

        if (sub is null)
        {
            sub = await CreateSubscription(ctx, checkout, customerSelector);
            if (sub is null)
                return;
        }

        if (sub.TrialEnd is not null)
            return;
        sub.PlanId = checkout.PlanId;
        sub.GracePeriodEnd = null;
        sub.PeriodEnd = null;
        sub.TrialEnd = now.AddDays(sub.Plan.TrialDays);
        await ctx.SaveChangesAsync(cancellationToken);

        await UpdateSubscriptionStates(ctx, new MemberSelector.Single(sub.Id), cancellationToken);
    }

    private async Task ProcessSubscriptionPayment(InvoiceEntity invoice, string checkoutId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;

        bool needUpdate = false;
        await using var ctx = applicationDbContextFactory.CreateContext();
        var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutId);
        var plan = checkout?.Plan;
        if (checkout is null || plan is null ||
            (invoice.Status == InvoiceStatus.Processing && !plan.OptimisticActivation) ||
            checkout.Plan.Offering.App.StoreDataId != invoice.StoreId)
            return;

        // If subscrberId isn't set, then it should be a new subscriber.
        if (checkout.Subscriber is null && !checkout.NewSubscriber)
            throw new InvalidOperationException("Bug: Subscriber is null and not a new subscriber");

        var sub = checkout.Subscriber;

        if (invoice.Status is InvoiceStatus.Settled or InvoiceStatus.Processing)
        {
            // We only create a new subscriber lazily when a payment has been received
            if (sub is null)
            {
                sub = await CreateSubscription(ctx, checkout, CustomerSelector.ByEmail(invoice.Metadata.BuyerEmail));
                if (sub is null)
                    return;
                invoice.SubscriberId = sub.Id;
                invoice.CustomerId = sub.CustomerId;
                var inv = await ctx.Invoices.FindAsync([invoice.Id], cancellationToken) ?? throw new InvalidOperationException("Invoice not found");
                inv.SetBlob(invoice);
                await ctx.SaveChangesAsync(cancellationToken);
            }

            if (invoice.Status == InvoiceStatus.Processing)
            {
                // We need to store the data ahead of the optimistic activation.
                // in case the invoice fails to confirm later, we can use this to roll back the activation to the previous state.
                await SaveRollbackData(new OptimisticActivationData()
                {
                    PeriodEnd = sub.PeriodEnd,
                    TrialEnd = sub.TrialEnd,
                    GracePeriodEnd = sub.GracePeriodEnd
                }, ctx, sub);
            }

            var p = sub.Plan.GetNextPeriodEnd(sub switch
            {
                { Phase: PhaseTypes.Trial, TrialEnd: { } te } => te,
                { Phase: PhaseTypes.Grace, PeriodEnd: { } pe } => pe,
                { Phase: PhaseTypes.Normal, PeriodEnd: { } pe } => pe,
                { Phase: PhaseTypes.Expired } => now,
                _ => now
            });
            sub.PeriodEnd = p.PeriodEnd;
            sub.TrialEnd = null;
            sub.GracePeriodEnd = p.PeriodGraceEnd;

            if (invoice.Status == InvoiceStatus.Settled)
            {
                await RemoveRollbackData(ctx, sub);
            }

            needUpdate = true;
            await ctx.SaveChangesAsync(cancellationToken);
        }
        else if (invoice.Status == InvoiceStatus.Invalid && sub is not null)
        {
            var rollbackData = OptimisticActivationData.GetAdditionalData(sub);
            if (rollbackData is null)
                return;
            sub.PeriodEnd = rollbackData.PeriodEnd;
            sub.TrialEnd = rollbackData.TrialEnd;
            sub.GracePeriodEnd = rollbackData.GracePeriodEnd;
            await RemoveRollbackData(ctx, sub);
            needUpdate = true;
            await ctx.SaveChangesAsync(cancellationToken);
        }

        if (needUpdate && sub is not null)
            await UpdateSubscriptionStates(ctx, new MemberSelector.Single(sub.Id), cancellationToken);
    }

    private async Task<SubscriberData?> CreateSubscription(ApplicationDbContext ctx, PlanCheckoutData checkout, CustomerSelector customerSelector)
    {
        var plan = checkout.Plan;
        var cust = await ctx.Customers.GetOrUpdate(checkout.Plan.Offering.App.StoreDataId, customerSelector);
        (var sub, var created) = await ctx.Subscribers.GetOrCreateByCustomerId(cust.Id, plan.OfferingId, plan.Id, JObject.Parse(checkout.NewSubscriberMetadata));
        if (!created || sub is null)
            return null;
        await UpdatePlanMemberCount(ctx, plan.Id);
        return sub;
    }

    private static async Task RemoveRollbackData(ApplicationDbContext ctx, SubscriberData subscriber)
    {
        await ctx.Database.GetDbConnection()
            .ExecuteAsync("""
                          UPDATE subscriptions_subscribers
                          SET additional_data = additional_data - @key
                          WHERE id = @id
                          """, new { key = OptimisticActivationData.Key, memberId = subscriber.Id });
    }

    private static async Task SaveRollbackData(OptimisticActivationData rollbackData, ApplicationDbContext ctx,
        SubscriberData subscriber)
    {
        var data = JsonConvert.SerializeObject(rollbackData, BaseEntityData.Settings);
        await ctx.Database.GetDbConnection()
            .ExecuteAsync("""
                          UPDATE subscriptions_subscribers
                          SET additional_data = additional_data || jsonb_build_object(@key, @value::JSONB)
                          WHERE id = @id
                          """, new
            {
                key = OptimisticActivationData.Key,
                value = data,
                id = subscriber.Id
            });
    }
    private static async Task UpdatePlanMemberCount(ApplicationDbContext ctx, string planId)
    {
        await ctx.Database.GetDbConnection()
            .ExecuteAsync("""
                          UPDATE subscriptions_plans
                          SET members_count = (SELECT COUNT(*) FROM subscriptions_subscribers WHERE plan_id = @id AND active)
                          WHERE id = @id
                          """, new
            {
                id = planId
            });
    }
}
