#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
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
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using static BTCPayServer.Data.Subscriptions.SubscriberData;

namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriptionHostedService(
    EventAggregator eventAggregator,
    ApplicationDbContextFactory applicationDbContextFactory,
    SettingsRepository settingsRepository,
    Logs logger) : EventHostedServiceBase(eventAggregator, logger), IPeriodicTask
{
    record Poll;

    public record StartTrial(string StoreId, string PlanId, string Email);
    public Task Do(CancellationToken cancellationToken)
        => base.RunEvent(new Poll(), cancellationToken);
    protected override void SubscribeToEvents()
    {
        this.Subscribe<Events.InvoiceEvent>();
    }

    public Task ExecuteStartTrial(StartTrial startTrial, CancellationToken cancellationToken)
    => this.RunEvent(startTrial, cancellationToken);

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
            UIStoreSubscriptionsController.GetPlanIdFromInvoice(settledInvoice) is string planId)
        {
            await ProcessSubscriptionPayment(settledInvoice, planId, cancellationToken);
        }
        else if (evt is Poll)
        {
            var from = (await settingsRepository.GetSettingAsync<MembershipServerSettings>("MembershipHostedService"))?.LastUpdate;
            var to = DateTimeOffset.UtcNow;
            await UpdateSubscriptionStates(new MemberSelector.PassedDate(from, to), cancellationToken);
            await settingsRepository.UpdateSetting(new MembershipServerSettings(to), "MembershipHostedService");
        }
        else if (evt is StartTrial trial)
        {
            await ProcessStartTrial(trial, cancellationToken);
        }
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
        public record Single(string CustomerId) : MemberSelector
        {
            public override IQueryable<SubscriberData> Where(IQueryable<SubscriberData> query)
                => query.Where(m => m.CustomerId == CustomerId);
        }

        public record PassedDate(DateTimeOffset? From, DateTimeOffset To) : MemberSelector
        {
            public override IQueryable<SubscriberData> Where(IQueryable<SubscriberData> query)
                => From is null ?
                    query.Where(q => (q.PeriodEnd < To || q.GracePeriodEnd < To || q.TrialEnd < To)) :
                    query.Where(q => (q.PeriodEnd >= From && q.PeriodEnd < To) || (q.GracePeriodEnd >= From && q.GracePeriodEnd < To) || (q.TrialEnd >= From && q.TrialEnd < To));

        }

        public abstract IQueryable<SubscriberData> Where(IQueryable<SubscriberData> query);

    }

    private async Task UpdateSubscriptionStates(MemberSelector selector, CancellationToken cancellationToken)
    {
        List<SubscriptionEvent> events = new();
        await using var ctx = applicationDbContextFactory.CreateContext();
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

    private async Task ProcessStartTrial(StartTrial trial, CancellationToken cancellationToken)
    {
        var now = DateTimeOffset.UtcNow;
        string customerId;
        await using (var ctx = applicationDbContextFactory.CreateContext())
        {
            var plan = await ctx.Plans.FindAsync([trial.PlanId], cancellationToken);
            if (plan is null)
                return;
            var cust = await ctx.Customers.GetOrUpdate(trial.StoreId, trial.Email);
            customerId = cust.Id;
            var sub = await ctx.Subscribers.GetOrCreateByCustomerId(cust.Id, plan.OfferingId, trial.PlanId) ??
                      await ctx.Subscribers.GetByCustomerId(cust.Id, plan.OfferingId); // it may be on a different plan
            if (sub is null)
                return;

            sub.PlanId = trial.PlanId;
            sub.GracePeriodEnd = null;
            sub.PeriodEnd = null;
            sub.TrialEnd = now.AddDays(plan.TrialDays);
            await ctx.SaveChangesAsync(cancellationToken);
        }
        await UpdateSubscriptionStates(new MemberSelector.Single(customerId), cancellationToken);
    }

    private async Task ProcessSubscriptionPayment(InvoiceEntity invoice, string planId, CancellationToken cancellationToken = default)
    {
        var now = DateTimeOffset.UtcNow;
        string customerId;
        bool needUpdate = false;
        await using (var ctx = applicationDbContextFactory.CreateContext())
        {
            var plan = await ctx.Plans.FindAsync([planId], cancellationToken);
            if (plan is null ||
                (invoice.Status == InvoiceStatus.Processing && !plan.OptimisticActivation))
                return;

            var cust = await ctx.Customers.GetOrUpdate(invoice.StoreId, invoice.Metadata.BuyerEmail);
            customerId = cust.Id;
            var sub = await ctx.Subscribers.GetOrCreateByCustomerId(cust.Id, plan.OfferingId, planId);
            if (sub is null)
                return;
            if (invoice.Status is InvoiceStatus.Settled or InvoiceStatus.Processing)
            {
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
                    { Phase: PhaseTypes.Trial, TrialEnd: {} te } => te,
                    { Phase: PhaseTypes.Grace, PeriodEnd: {} pe } => pe,
                    { Phase: PhaseTypes.Normal, PeriodEnd: {} pe } => pe,
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
            else if (invoice.Status == InvoiceStatus.Invalid)
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
        }

        if (needUpdate)
            await UpdateSubscriptionStates(new MemberSelector.Single(customerId), cancellationToken);
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
                          """, new {
                key = OptimisticActivationData.Key,
                value = data,
                id = subscriber.Id });
    }
}
