#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Controllers;
using BTCPayServer.Data;
using BTCPayServer.Events;
using BTCPayServer.JsonConverters;
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using Dapper;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using static BTCPayServer.Data.SubscriptionMemberData;

namespace BTCPayServer.HostedServices;

public class MembershipHostedService(
    EventAggregator eventAggregator,
    ApplicationDbContextFactory applicationDbContextFactory,
    SettingsRepository SettingsRepository,
    Logs logger) : EventHostedServiceBase(eventAggregator, logger), IPeriodicTask
{
    record Poll;

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
            UIStoreMembershipController.GetPlanIdFromInvoice(settledInvoice) is string planId)
        {
            await ProcessSubscriptionPayment(settledInvoice, planId, cancellationToken);
        }
        else if (evt is Poll)
        {
            var from = (await SettingsRepository.GetSettingAsync<MembershipServerSettings>("MembershipHostedService"))?.LastUpdate;
            var to = DateTimeOffset.UtcNow;
            await UpdateSubscriptionStates(new MemberSelector.PassedDate(from, to), cancellationToken);
            await SettingsRepository.UpdateSetting(new MembershipServerSettings(to), "MembershipHostedService");
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
            public override IQueryable<SubscriptionMemberData> Where(IQueryable<SubscriptionMemberData> query)
                => query.Where(m => m.CustomerId == CustomerId);
        }

        public record PassedDate(DateTimeOffset? From, DateTimeOffset To) : MemberSelector
        {
            public override IQueryable<SubscriptionMemberData> Where(IQueryable<SubscriptionMemberData> query)
                => From is null ?
                    query.Where(q => (q.PeriodEnd < To || q.GracePeriodEnd < To || q.TrialEnd < To)) :
                    query.Where(q => (q.PeriodEnd >= From && q.PeriodEnd < To) || (q.GracePeriodEnd >= From && q.GracePeriodEnd < To) || (q.TrialEnd >= From && q.TrialEnd < To));

        }

        public abstract IQueryable<SubscriptionMemberData> Where(IQueryable<SubscriptionMemberData> query);

    }

    private async Task UpdateSubscriptionStates(MemberSelector selector, CancellationToken cancellationToken)
    {
        List<SubscriptionEvent> events = new();
        await using var ctx = applicationDbContextFactory.CreateContext();
        var query = ctx.Members.Include(m => m.Plan).Include(m => m.Customer);
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

    private async Task ProcessSubscriptionPayment(InvoiceEntity invoice, string planId, CancellationToken cancellationToken = default)
    {
        string customerId;
        bool needUpdate = false;
        await using (var ctx = applicationDbContextFactory.CreateContext())
        {
            var plan = await ctx.SubscriptionPlans.FindAsync([planId], cancellationToken);
            if (plan is null ||
                (invoice.Status == InvoiceStatus.Processing && !plan.OptimisticActivation))
                return;

            var cust = await ctx.Customers.GetOrUpdate(invoice.StoreId, invoice.Metadata.BuyerEmail);
            customerId = cust.Id;
            var member = await ctx.Members.GetOrCreateMemberByCustomerId(cust.Id, planId);
            if (member is null)
                return;
            if (invoice.Status is InvoiceStatus.Settled or InvoiceStatus.Processing)
            {
                if (member.Plan.Renewable)
                {
                    if (invoice.Status == InvoiceStatus.Processing)
                    {
                        // We need to store the data ahead of the optimistic activation.
                        // in case the invoice fails to confirm later, we can use this to roll back the activation to the previous state.
                        await SaveRollbackData(new OptimisticActivationData()
                        {
                            PeriodEnd = member.PeriodEnd,
                            TrialEnd = member.TrialEnd,
                            GracePeriodEnd = member.GracePeriodEnd
                        }, ctx, member);
                    }

                    var p = member.Plan.GetNextPeriodEnd(member switch
                        {
                            { Phase: PhaseTypes.Trial, TrialEnd: {} te } => te,
                            { Phase: PhaseTypes.Grace, PeriodEnd: {} pe } => pe,
                            { Phase: PhaseTypes.Normal, PeriodEnd: {} pe } => pe,
                            { Phase: PhaseTypes.Expired } => DateTimeOffset.UtcNow,
                            _ => DateTimeOffset.UtcNow
                        });
                    member.PeriodEnd = p.PeriodEnd;
                    member.TrialEnd = null;
                    member.GracePeriodEnd = p.PeriodGraceEnd;

                    if (invoice.Status == InvoiceStatus.Settled)
                    {
                        await RemoveRollbackData(ctx, member);
                    }

                    needUpdate = true;
                    await ctx.SaveChangesAsync(cancellationToken);
                }
            }
            else if (invoice.Status == InvoiceStatus.Invalid)
            {
                var rollbackData = OptimisticActivationData.GetAdditionalData(member);
                if (rollbackData is null)
                    return;
                member.PeriodEnd = rollbackData.PeriodEnd;
                member.TrialEnd = rollbackData.TrialEnd;
                member.GracePeriodEnd = rollbackData.GracePeriodEnd;
                await RemoveRollbackData(ctx, member);
                needUpdate = true;
                await ctx.SaveChangesAsync(cancellationToken);
            }
        }

        if (needUpdate)
            await UpdateSubscriptionStates(new MemberSelector.Single(customerId), cancellationToken);
    }

    private static async Task RemoveRollbackData(ApplicationDbContext ctx, SubscriptionMemberData member)
    {
        await ctx.Database.GetDbConnection()
            .ExecuteAsync("""
                          UPDATE subscription_members
                          SET additional_data = additional_data - @key
                          WHERE customer_id = @memberId
                          """, new { key = OptimisticActivationData.Key, memberId = member.CustomerId });
    }

    private static async Task SaveRollbackData(OptimisticActivationData rollbackData, ApplicationDbContext ctx,
        SubscriptionMemberData member)
    {
        var data = JsonConvert.SerializeObject(rollbackData, BaseEntityData.Settings);
        await ctx.Database.GetDbConnection()
            .ExecuteAsync("""
                          UPDATE subscription_members
                          SET additional_data = additional_data || jsonb_build_object(@key, @value::JSONB)
                          WHERE customer_id = @memberId
                          """, new {
                key = OptimisticActivationData.Key,
                value = data,
                memberId = member.CustomerId });
    }
}
