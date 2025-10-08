#nullable enable
using System;
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
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Dapper;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static BTCPayServer.Data.Subscriptions.SubscriberData;

// ReSharper disable MethodSupportsCancellation

namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriptionHostedService(
    EventAggregator eventAggregator,
    ApplicationDbContextFactory applicationDbContextFactory,
    SettingsRepository settingsRepository,
    UIInvoiceController invoiceController,
    CurrencyNameTable currencyNameTable,
    LinkGenerator linkGenerator,
    Logs logger) : EventHostedServiceBase(eventAggregator, logger), IPeriodicTask
{
    record Poll;

    public record SubscribeRequest(string CheckoutId, CustomerSelector CustomerSelector);

    public Task Do(CancellationToken cancellationToken)
        => base.RunEvent(new Poll(), cancellationToken);

    protected override void SubscribeToEvents()
    {
        this.Subscribe<InvoiceEvent>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        await using var subCtx = CreateContext(cancellationToken);
        if (evt is InvoiceEvent
            {
                EventCode: InvoiceEventCode.Completed or
                InvoiceEventCode.MarkedCompleted or
                InvoiceEventCode.MarkedInvalid or
                InvoiceEventCode.PaidInFull,
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
            var to = subCtx.Now;
            await UpdateSubscriptionStates(subCtx, new MemberSelector.PassedDate(from, to));
            await settingsRepository.UpdateSetting(new MembershipServerSettings(to), "MembershipHostedService");
        }
        else if (evt is SubscribeRequest subscribeRequest)
        {
            var ctx = subCtx.Context;
            var checkout = await ctx.PlanCheckouts.GetCheckout(subscribeRequest.CheckoutId);
            if (checkout?.IsExpired is not false)
                throw new InvalidOperationException("Checkout not found or expired");
            var redirectLink =
                checkout.GetRedirectUrl() ??
                checkout.Plan.Offering.SuccessRedirectUrl ??
                linkGenerator.PlanCheckoutDefaultLink(checkout.BaseUrl);
            if (checkout.SuccessRedirectUrl != redirectLink)
            {
                checkout.SuccessRedirectUrl = redirectLink;
                await ctx.SaveChangesAsync();
            }

            if (checkout.IsTrial)
            {
                await StartPlanCheckoutWithoutInvoice(subCtx, checkout, subscribeRequest.CustomerSelector);
            }
            else
            {
                await CreateInvoiceForCheckout(subCtx, checkout, subscribeRequest.CustomerSelector);
            }
        }
        else if (evt is SuspendRequest suspendRequest)
        {
            var ctx = subCtx.Context;
            var sub = await ctx.Subscribers.FindAsync([suspendRequest.SubId], cancellationToken);
            if (sub is null)
                throw new InvalidOperationException("Subscriber not found");
            sub.IsSuspended = suspendRequest.Suspended ?? !sub.IsSuspended;
            sub.SuspensionReason = sub.IsSuspended ? suspendRequest.SuspensionReason : null;
            await ctx.SaveChangesAsync();
            await UpdateSubscriptionStates(subCtx, suspendRequest.SubId);
        }
        else if (evt is MoveTimeRequest move)
        {
            var ctx = subCtx.Context;
            var members = await move.MemberSelector.Where(ctx.Subscribers.IncludeAll()).ToListAsync(cancellationToken);

            foreach (var member in members)
            {
                if (member.PeriodEnd is not null)
                    member.PeriodEnd -= move.Period;
                if (member.TrialEnd is not null)
                    member.TrialEnd -= move.Period;
                if (member.GracePeriodEnd is not null)
                    member.GracePeriodEnd -= move.Period;
                member.PlanStarted -= move.Period;
            }

            await ctx.SaveChangesAsync();
            await UpdateSubscriptionStates(subCtx, move.MemberSelector);
        }
    }

    SubscriptionContext CreateContext() => CreateContext(CancellationToken);

    SubscriptionContext CreateContext(CancellationToken cancellationToken) =>
        new(applicationDbContextFactory.CreateContext(), EventAggregator, currencyNameTable, cancellationToken);

    public static string GetCheckoutPlanTag(string checkoutId) => $"SUBS#{checkoutId}";
    public static string? GetCheckoutPlanIdFromInvoice(InvoiceEntity invoiceEntiy) => invoiceEntiy.GetInternalTags("SUBS#").FirstOrDefault();

    private async Task CreateInvoiceForCheckout(SubscriptionContext subCtx, PlanCheckoutData checkout, CustomerSelector customerSelector, decimal? price = null)
    {
        var invoiceMetadata = JObject.Parse(checkout.InvoiceMetadata);
        if (checkout.NewSubscriber)
        {
            invoiceMetadata["planId"] = checkout.PlanId;
            invoiceMetadata["offeringId"] = checkout.Plan.OfferingId;
        }

        var mergedInvoiceMetadata = JObject.Parse(checkout.InvoiceMetadata);
        mergedInvoiceMetadata.Merge(invoiceMetadata);
        invoiceMetadata = mergedInvoiceMetadata;

        var plan = checkout.Plan;
        var existingCredit = checkout.Subscriber?.GetCredit() ?? 0m;
        var amount = price ?? (plan.Price - existingCredit);
        if (checkout.Subscriber?.GetUnusedPeriodAmount(subCtx.Now) is decimal unusedAmount)
            amount -= unusedAmount;
        amount = Math.Max(amount, 0);

        if (amount > 0)
        {
            var request = await invoiceController.CreateInvoiceCoreRaw(new()
                {
                    Currency = plan.Currency,
                    Amount = amount,
                    Checkout = new()
                    {
                        RedirectAutomatically = true,
                        RedirectURL = checkout.GetRedirectUrl()
                    },
                    Metadata = invoiceMetadata
                }, plan.Offering.App.StoreData, checkout.BaseUrl.ToString(),
                [GetCheckoutPlanTag(checkout.Id)], CancellationToken.None,
                entity =>
                {
                    entity.SubscriberId = checkout.SubscriberId;
                    entity.CustomerId = checkout.Subscriber?.CustomerId;
                });
            checkout.InvoiceId = request.Id;
            await subCtx.Context.SaveChangesAsync();
        }
        else
        {
            await StartPlanCheckoutWithoutInvoice(subCtx, checkout, customerSelector);
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

    public abstract record MemberSelector
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

    private async Task UpdateSubscriptionStates(SubscriptionContext subCtx, long subscriberId)
        => await UpdateSubscriptionStates(subCtx, new MemberSelector.Single(subscriberId));

    private async Task UpdateSubscriptionStates(SubscriptionContext subCtx, MemberSelector selector)
    {
        var (now, ctx, cancellationToken) = (subCtx.Now, subCtx.Context, subCtx.CancellationToken);
        var query = ctx.Subscribers.IncludeAll();
        var members = await selector.Where(query).ToListAsync(cancellationToken);
        await ctx.PlanEntitlements.FetchPlanEntitlementsAsync(members.Select(m => m.Plan));
        foreach (var m in members)
        {
            var newPhase = m.GetExpectedPhase(now);
            var (prevPhase, prevActive) = (m.Phase, m.IsActive);
            if (prevPhase != newPhase)
            {
                if (!m.IsSuspended &&
                    newPhase is PhaseTypes.Expired or PhaseTypes.Grace &&
                    m.NextPlan.Status is PlanData.PlanStatus.Active &&
                    m.AutoRenew)
                {
                    if (await subCtx.TryChargeSubscriber(m, m.NextPlan.Price))
                    {
                        newPhase = PhaseTypes.Normal;
                        var planBefore = m.PlanId;
                        m.StartNextPlan(now);
                        subCtx.AddEvent(new SubscriptionEvent.PlanStarted(m)
                        {
                            AutoRenew = planBefore == m.PlanId
                        });
                    }
                }

                if (newPhase is PhaseTypes.Expired)
                {
                    m.PaidAmount = null;
                    if (m is { NewPlan: not null, NewPlanId: not null } && m.NewPlanId != m.PlanId)
                    {
                        (m.PlanId, m.Plan) = (m.NewPlanId, m.NewPlan);
                        (m.NewPlanId, m.NewPlan) = (null, null);
                    }
                }

                if (prevPhase != newPhase)
                {
                    m.Phase = newPhase;
                    subCtx.AddEvent(new SubscriptionEvent.SubscriberPhaseChanged(m, prevPhase));
                }
            }

            var needReminder = m.GetReminderDate() <= now &&
                               !m.PaymentReminded &&
                               m.MissingCredit() >= 0m;
            if (needReminder)
            {
                m.PaymentReminded = true;
                subCtx.AddEvent(new SubscriptionEvent.PaymentReminder(m));
            }

            var newActive = !m.IsSuspended && newPhase != PhaseTypes.Expired;

            if (prevActive != newActive)
            {
                m.IsActive = newActive;
                if (newActive)
                    subCtx.AddEvent(new SubscriptionEvent.SubscriberActivated(m));
                else
                    subCtx.AddEvent(new SubscriptionEvent.SubscriberDisabled(m));
            }
        }

        await ctx.SaveChangesAsync(cancellationToken);

        foreach (var plan in subCtx.Events.OfType<SubscriptionEvent.SubscriberEvent>().Select(x => x.Subscriber.PlanId).Distinct())
        {
            await UpdatePlanMemberCount(ctx, plan);
        }
    }

    public Task ProceedToSubscribe(string checkoutId, CustomerSelector selector, CancellationToken cancellationToken)
    => RunEvent(new SubscribeRequest(checkoutId, selector), cancellationToken);

    private async Task StartPlanCheckoutWithoutInvoice(SubscriptionContext subCtx, PlanCheckoutData checkout, CustomerSelector customerSelector)
    {
        var ctx = subCtx.Context;
        var sub = checkout.Subscriber;
        if (sub is null && !checkout.NewSubscriber)
            throw new InvalidOperationException("Bug: Subscriber is null and not a new subscriber");

        if (sub is null)
        {
            sub = await CreateSubscription(subCtx, checkout, false, customerSelector);
            if (sub is null)
                return;
        }

        await TryStartPlan(subCtx, checkout, sub);

        await ctx.SaveChangesAsync();
        await UpdateSubscriptionStates(subCtx, sub.Id);
    }

    private async Task ProcessSubscriptionPayment(InvoiceEntity invoice, string checkoutId, CancellationToken cancellationToken = default)
    {
        bool needUpdate = false;
        await using var subCtx = CreateContext(cancellationToken);
        var ctx = subCtx.Context;
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
                var optimisticActivation = invoice.Status == InvoiceStatus.Processing && plan.OptimisticActivation;
                sub = await CreateSubscription(subCtx, checkout, optimisticActivation, CustomerSelector.ByEmail(invoice.Metadata.BuyerEmail));
                if (sub is null)
                    return;
                invoice.SubscriberId = sub.Id;
                invoice.CustomerId = sub.CustomerId;
                var inv = await ctx.Invoices.FindAsync([invoice.Id], cancellationToken) ?? throw new InvalidOperationException("Invoice not found");
                inv.SetBlob(invoice);
                await ctx.SaveChangesAsync();
            }

            var invoiceCredit = subCtx.GetAmountToCredit(invoice);
            if (checkout.Credited != invoiceCredit)
            {
                var diff = invoiceCredit - checkout.Credited;
                if (diff > 0)
                {
                    checkout.Credited += diff;
                    await subCtx.CreditSubscriber(sub, diff);

                    if (!checkout.PlanStarted)
                    {
                        await TryStartPlan(subCtx, checkout, sub);
                    }
                }
                else
                {
                    await subCtx.TryChargeSubscriber(sub, -diff, force: true);
                    checkout.Credited -= -diff;
                }
            }

            if (invoice.Status == InvoiceStatus.Settled && sub.Plan.OptimisticActivation)
                // Maybe we don't need this column
                sub.OptimisticActivation = false;

            needUpdate = true;
            await ctx.SaveChangesAsync();
        }
        else if (sub is not null && invoice.Status == InvoiceStatus.Invalid &&
                 checkout is { PlanStarted: true, Credited: not 0m })
        {
            // We should probably ask the merchant before reversing the credit...
            // await TryChargeSubscriber(ctx, sub, checkout.Credited, force: true);
            // checkout.Credited = 0m;
            sub.IsSuspended = true;
            sub.SuspensionReason = "The plan has been started by an invoice which later became invalid.";
            needUpdate = true;
            await ctx.SaveChangesAsync();
        }

        if (sub is not null)
        {
            if (needUpdate)
                await UpdateSubscriptionStates(subCtx, sub.Id);
        }
    }

    private async Task TryStartPlan(SubscriptionContext subCtx, PlanCheckoutData checkout, SubscriberData sub)
    {
        if (checkout.PlanStarted)
            return;
        var now = subCtx.Now;
        using var scope = sub.NewPlanScope(checkout.Plan);
        if (sub.CanStartNextPlan)
        {
            if (checkout.IsTrial || await subCtx.TryChargeSubscriber(sub, sub.NextPlan.Price))
            {
                sub.StartNextPlan(now, checkout.IsTrial);
                scope.Commit();
                checkout.PlanStarted = true;
            }
        }
        // In hard migrations, we stop the current plan by reimbursing what has
        // not yet been spent. The we start the new plan.
        else if (checkout.OnPay == PlanCheckoutData.OnPayBehavior.HardMigration)
        {
            var unusedAmount = subCtx.RoundAmount(sub.GetUnusedPeriodAmount(now) ?? 0.0m, sub.Plan.Currency);

            if (checkout.IsTrial || await subCtx.TryCreditChargeSubscriber(sub, unusedAmount, sub.NextPlan.Price) is not null)
            {
                checkout.RefundAmount = unusedAmount;
                sub.StartNextPlan(now, checkout.IsTrial);
                scope.Commit();
                checkout.PlanStarted = true;
            }
        }
        subCtx.AddEvent(new SubscriptionEvent.PlanStarted(sub));
    }

    record MoveTimeRequest(MemberSelector MemberSelector, TimeSpan Period);

    /// <summary>
    /// Modify the dates of the subscriber so that he moves to the next phase.
    /// </summary>
    /// <param name="memberSelector"></param>
    /// <param name="period"></param>
    /// <returns></returns>
    public Task MoveTime(MemberSelector memberSelector, TimeSpan period)
        => this.RunEvent(new MoveTimeRequest(memberSelector, period));

    public async Task MoveTime(long subscriberId, PhaseTypes phase)
    {
        await using var ctx = applicationDbContextFactory.CreateContext();
        var selector = new MemberSelector.Single(subscriberId);
        var subscriber = await selector.Where(ctx.Subscribers.IncludeAll()).FirstAsync();
        TimeSpan time;
        if (phase == PhaseTypes.Normal)
            time = subscriber.TrialEnd!.Value - DateTimeOffset.UtcNow;
        else if (phase == PhaseTypes.Grace)
            time = subscriber.PeriodEnd!.Value - DateTimeOffset.UtcNow;
        else if (phase == PhaseTypes.Expired)
            time = subscriber.GracePeriodEnd!.Value - DateTimeOffset.UtcNow;
        else
            throw new InvalidOperationException("Invalid phase");

        await this.MoveTime(selector, time);
    }

    private async Task<SubscriberData?> CreateSubscription(SubscriptionContext subCtx, PlanCheckoutData checkout, bool optimisticActivation,
        CustomerSelector customerSelector)
    {
        var ctx = subCtx.Context;
        var plan = checkout.Plan;
        var cust = await ctx.Customers.GetOrUpdate(checkout.Plan.Offering.App.StoreDataId, customerSelector);
        (var sub, var created) =
            await ctx.Subscribers.GetOrCreateByCustomerId(cust.Id, plan.OfferingId, plan.Id, optimisticActivation, checkout.TestAccount,
                JObject.Parse(checkout.NewSubscriberMetadata));
        if (!created || sub is null)
            return null;
        checkout.Subscriber = sub;
        checkout.SubscriberId = sub.Id;
        await ctx.SaveChangesAsync();
        subCtx.AddEvent(new SubscriptionEvent.NewSubscriber(sub));
        return sub;
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

    record SuspendRequest(long SubId, string? SuspensionReason, bool? Suspended);

    public Task ToggleSuspend(long subId, string? suspensionReason)
        => RunEvent(new SuspendRequest(subId, suspensionReason, null));

    public Task Suspend(long subId, string? suspensionReason)
        => RunEvent(new SuspendRequest(subId, suspensionReason, true));

    public async Task UpdateCredit(long subscriberId, decimal update)
    {
        await using var subCtx = CreateContext();
        var sub = await subCtx.Context.Subscribers.GetById(subscriberId);
        if (sub is null) return;
        if (update < 0)
            await subCtx.TryChargeSubscriber(sub, -update, force: true);
        else if (update > 0)
            await subCtx.CreditSubscriber(sub, update);
    }


    public async Task<string?> CreateCreditCheckout(string portalSessionId, decimal? value)
    {
        await using var subCtx = CreateContext(CancellationToken);
        var ctx = subCtx.Context;
        var portal = await ctx.PortalSessions.GetActiveById(portalSessionId);
        if (portal is null)
            return null;

        var checkout = new PlanCheckoutData(portal.Subscriber)
        {
            SuccessRedirectUrl = linkGenerator.SubscriberPortalLink(portalSessionId, portal.BaseUrl),
            BaseUrl = portal.BaseUrl
        };
        ctx.PlanCheckouts.Add(checkout);
        await ctx.SaveChangesAsync();
        await this.CreateInvoiceForCheckout(subCtx, checkout, portal.Subscriber.CustomerSelector, value);
        return checkout.InvoiceId;
    }

    public async Task<string?> CreatePlanMigrationCheckout(string portalSessionId, string? planId, PlanCheckoutData.OnPayBehavior migrationType,
        RequestBaseUrl requestBaseUrl)
    {
        await using var ctx = applicationDbContextFactory.CreateContext();
        var portal = await ctx.PortalSessions.GetActiveById(portalSessionId);
        var plan = planId is null ? null : await ctx.Plans.GetPlanFromId(planId);
        if (portal is null)
            return null;
        var checkout = new PlanCheckoutData(portal.Subscriber, plan)
        {
            SuccessRedirectUrl = linkGenerator.SubscriberPortalLink(portalSessionId, requestBaseUrl),
            OnPay = migrationType,
            BaseUrl = requestBaseUrl,
        };
        ctx.PlanCheckouts.Add(checkout);
        await ctx.SaveChangesAsync();
        return checkout.Id;
    }
}
