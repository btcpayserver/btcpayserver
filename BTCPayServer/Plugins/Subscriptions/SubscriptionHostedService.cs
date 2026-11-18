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
using BTCPayServer.Logging;
using BTCPayServer.Services;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Dapper;
using Microsoft.AspNetCore.Routing;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using static BTCPayServer.Data.Subscriptions.SubscriberData;

// ReSharper disable MethodSupportsCancellation

namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriptionHostedService(
    EventAggregator eventAggregator,
    PullPaymentHostedService pullPaymentService,
    ApplicationDbContextFactory applicationDbContextFactory,
    SettingsRepository settingsRepository,
    IServiceScopeFactory scopeFactory,
    CurrencyNameTable currencyNameTable,
    LinkGenerator linkGenerator,
    Logs logger) : EventHostedServiceBase(eventAggregator, logger), IPeriodicTask
{
    record Poll;

    public record SubscribeRequest(string CheckoutId);

    public Task Do(CancellationToken cancellationToken)
        => base.RunEvent(new Poll(), cancellationToken);

    protected override void SubscribeToEvents()
    {
        this.Subscribe<InvoiceEvent>();
        this.Subscribe<SubscriptionEvent.PlanUpdated>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        await using var subCtx = CreateContext(cancellationToken);
        if (evt is InvoiceEvent
            {
                EventCode: InvoiceEventCode.Completed or
                InvoiceEventCode.MarkedCompleted or
                InvoiceEventCode.MarkedInvalid or
                InvoiceEventCode.FailedToConfirm or
                InvoiceEventCode.Expired or
                InvoiceEventCode.PaidInFull,
                Invoice:
                {
                    Status: InvoiceStatus.Settled or InvoiceStatus.Invalid or InvoiceStatus.Processing or InvoiceStatus.Expired
                } settledInvoice
            } &&
            GetPlanCheckoutIdFromInvoice(settledInvoice) is string checkoutId)
        {
            await ProcessSubscriptionPayment(settledInvoice, checkoutId, cancellationToken);
        }
        else if (evt is Poll)
        {
            var from = (await settingsRepository.GetSettingAsync<MembershipServerSettings>("MembershipHostedService"))?.LastUpdate;
            var to = subCtx.Now;
            await UpdateSubscriptionStates(subCtx, new MemberSelector.PassedDate(from, to));
            if (subCtx.Events.Count != 0)
                await settingsRepository.UpdateSetting(new MembershipServerSettings(to), "MembershipHostedService");
        }
        else if (evt is SubscriptionEvent.PlanUpdated planUpdated)
        {
            await UpdatePlanStats(subCtx.Context, planUpdated.Plan.Id);
        }
        else if (evt is SubscribeRequest subscribeRequest)
        {
            var ctx = subCtx.Context;
            var checkout = await ctx.PlanCheckouts.GetCheckout(subscribeRequest.CheckoutId);
            if (checkout?.IsExpired is not false)
                throw new InvalidOperationException("Checkout not found or expired");
            if (checkout.Invoice is { Status: not (Data.InvoiceData.Expired or Data.InvoiceData.Invalid) })
                return;
            if (checkout.CreditedByInvoice != 0m)
                throw new InvalidOperationException("The previous checkout invoice has not been reconciled");
            if (checkout.Subscriber is { } subscriber &&
                !subscriber.Plan.Currency.Equals(checkout.Plan.Currency, StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException("The checkout's plan isn't in the same currency as the subscriber's plan");
            if (checkout.SuccessRedirectUrl is null)
            {
                var redirectLink = checkout.Plan.Offering.SuccessRedirectUrl ??
                                   linkGenerator.PlanCheckoutDefaultLink(checkout.BaseUrl);
                checkout.SuccessRedirectUrl = redirectLink;
                await ctx.SaveChangesAsync();
            }

            if (checkout.IsTrial)
            {
                await StartPlanCheckoutWithoutInvoice(subCtx, checkout);
            }
            else
            {
                await CreateInvoiceForCheckout(subCtx, checkout);
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
                if (member.ReminderDate is not null)
                    member.ReminderDate -= move.Period;
                member.PlanStarted -= move.Period;
            }

            await ctx.SaveChangesAsync();
            await UpdateSubscriptionStates(subCtx, move.MemberSelector);
        }
        else if (evt is UpdateDatesRequest datesRequest)
        {
            var ctx = subCtx.Context;
            var sub = await ctx.Subscribers.IncludeAll().FirstOrDefaultAsync(s => s.Id == datesRequest.SubId, cancellationToken);
            if (sub is null)
                throw new InvalidOperationException("Subscriber not found");

            sub.PlanStarted = datesRequest.StartDate;
            sub.PaymentReminded = false;

            if (datesRequest.ExpirationDate is { } expDate)
            {
                if (sub.TrialEnd is not null)
                {
                    sub.TrialEnd = expDate;
                    sub.ReminderDate = expDate - TimeSpan.FromDays(sub.PaymentReminderDaysOrDefault);
                }
                else if (sub.Plan.RecurringType != PlanData.RecurringInterval.Lifetime)
                {
                    sub.PeriodEnd = expDate;
                    sub.TrialEnd = null;
                    sub.GracePeriodEnd = sub.Plan.GracePeriodDays > 0 ? expDate.AddDays(PlanData.ClampDays(sub.Plan.GracePeriodDays)) : (DateTimeOffset?)null;
                    sub.ReminderDate = expDate - TimeSpan.FromDays(sub.PaymentReminderDaysOrDefault);
                }
            }

            await ctx.SaveChangesAsync(cancellationToken);
            await UpdateSubscriptionStates(subCtx, datesRequest.SubId);
        }
    }

    record UpdateDatesRequest(long SubId, DateTimeOffset StartDate, DateTimeOffset? ExpirationDate);

    public Task UpdateDates(long subId, DateTimeOffset startDate, DateTimeOffset? expirationDate)
        => RunEvent(new UpdateDatesRequest(subId, startDate, expirationDate));

    SubscriptionContext CreateContext() => CreateContext(CancellationToken);

    SubscriptionContext CreateContext(CancellationToken cancellationToken) =>
        new(applicationDbContextFactory.CreateContext(), EventAggregator, currencyNameTable, cancellationToken);

    public static string GetPlanCheckoutTag(string planCheckoutId) => $"SUBS#{planCheckoutId}";
    public static string? GetPlanCheckoutIdFromInvoice(InvoiceEntity invoiceEntiy) => invoiceEntiy.GetInternalTags("SUBS#").FirstOrDefault();

    private async Task CreateInvoiceForCheckout(SubscriptionContext subCtx, PlanCheckoutData checkout)
    {
        var invoiceMetadata = JsonParser.ParseObject(checkout.InvoiceMetadata);
        if (checkout.NewSubscriber)
        {
            invoiceMetadata["planId"] = checkout.PlanId;
            invoiceMetadata["offeringId"] = checkout.Plan.OfferingId;
        }
        if (checkout.GetEmail() is string email && !invoiceMetadata.ContainsKey("buyerEmail"))
            invoiceMetadata["buyerEmail"] = email;

        var plan = checkout.Plan;
        var existingCredit = checkout.Subscriber?.GetCredit() ?? 0m;
        var amount = checkout.CreditPurchase ?? (subCtx.RoundAmount(plan.Price, plan.Currency) - existingCredit);
        if (checkout.OnPay == PlanCheckoutData.OnPayBehavior.HardMigration &&
            checkout.Subscriber?.GetUnusedPeriodAmount(subCtx.Now) is decimal unusedAmount)
            amount -= subCtx.RoundAmount(unusedAmount, plan.Currency);
        amount = Math.Max(amount, 0);

        if (amount > 0)
        {
            using var scope = scopeFactory.CreateScope();
            var invoiceController = scope.ServiceProvider.GetRequiredService<UIInvoiceController>();
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
                [GetPlanCheckoutTag(checkout.Id)], CancellationToken.None);
            if (checkout.SubscriberId is not null)
            {
                subCtx.Context.SubscribersInvoices.Add(new()
                {
                    SubscriberId = checkout.SubscriberId.Value,
                    InvoiceId = request.Id
                });
            }
            checkout.InvoiceId = request.Id;
            await subCtx.Context.SaveChangesAsync();
        }
        else
        {
            await StartPlanCheckoutWithoutInvoice(subCtx, checkout);
        }
    }

    class MembershipServerSettings
    {
        // ReSharper disable once UnusedMember.Local
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
                    ? query.Where(q => (q.PeriodEnd < To || q.GracePeriodEnd < To || q.TrialEnd < To || q.ReminderDate < To))
                    : query.Where(q =>
                        (q.PeriodEnd >= From && q.PeriodEnd < To) || (q.GracePeriodEnd >= From && q.GracePeriodEnd < To) ||
                        (q.TrialEnd >= From && q.TrialEnd < To) || (q.ReminderDate >= From && q.ReminderDate < To));
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
        await ctx.PlanFeatures.FetchPlanFeaturesAsync(members.Select(m => m.Plan));
        foreach (var m in members)
        {
            var newPhase = m.GetExpectedPhase(now);
            var (prevPhase, prevActive) = (m.Phase, m.IsActive);
            if (prevPhase != newPhase)
            {
                if (newPhase is PhaseTypes.Expired or PhaseTypes.Grace && m.AutoRenew)
                {
                    if (m.CanStartNextPlanEx(false, newPhase))
                    {
                        if (await subCtx.TryChargeSubscriber(m, $"Auto renewal for plan '{m.NextPlan.Name}'", m.NextPlan.Price))
                        {
                            newPhase = PhaseTypes.Normal;
                            var planBefore = m.Plan;
                            m.StartNextPlan(now);
                            subCtx.AddEvent(new SubscriptionEvent.PlanStarted(m, planBefore)
                            {
                                AutoRenew = planBefore.Id == m.PlanId
                            });
                        }
                    }
                    else if (!m.IsSuspended)
                    {
                        subCtx.AddEvent(new SubscriptionEvent.NeedUpgrade(m));
                    }
                }

                if (newPhase is PhaseTypes.Expired or PhaseTypes.Grace && m is { NewPlan: not null, NewPlanId: not null } && m.NewPlanId != m.PlanId)
                {
                    var prevPlan = m.Plan;
                    (m.PlanId, m.Plan) = (m.NewPlanId, m.NewPlan);
                    (m.NewPlanId, m.NewPlan) = (null, null);
                    m.PaidAmount = null;
                    newPhase = m.GetExpectedPhase(now);
                    subCtx.AddEvent(new SubscriptionEvent.PlanStarted(m, prevPlan)
                    {
                        AutoRenew = false
                    });
                }

                if (newPhase is PhaseTypes.Expired)
                {
                    m.PaidAmount = null;
                }

                if (prevPhase != newPhase)
                {
                    m.Phase = newPhase;
                    subCtx.AddEvent(new SubscriptionEvent.SubscriberPhaseChanged(m, prevPhase));
                }
            }

            var needReminder = m.ReminderDate <= now &&
                               !m.PaymentReminded &&
                               m.MissingCredit() > 0m;
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
                    subCtx.AddEvent(new SubscriptionEvent.SubscriberDisabled(
                        m,
                        m.IsSuspended ? SubscriptionEvent.DisabledReason.Suspension : SubscriptionEvent.DisabledReason.Expired,
                        m.SuspensionReason));
            }
        }

        await ctx.SaveChangesAsync(cancellationToken);

        foreach (var plan in GetActiveMemberChangedPlans(subCtx))
        {
            await UpdatePlanStats(ctx, plan);
        }
    }

    public async Task<string?> CreateCreditRefund(long subscriberId, decimal amount, RequestBaseUrl requestBaseUrl, bool autoApprove)
    {
        await using var subCtx = CreateContext(CancellationToken);
        var ctx = subCtx.Context;
        var sub = await ctx.Subscribers.GetById(subscriberId);
        if (sub is null)
            return null;

        var credit = sub.GetCredit();
        if (amount <= 0 || amount > credit)
            return null;

        var store = await ctx.Stores.FindAsync(sub.Plan.Offering.App.StoreDataId);
        if (store is null)
            return null;

        var debitResult = await subCtx.TryCreditDebitSubscriber(sub, $"Credit refund (Subscriber Id: {subscriberId})", 0m, amount);
        if (debitResult is null)
            return null;

        var pullPaymentId = await pullPaymentService.CreatePullPayment(store, new CreatePullPaymentRequest
        {
            Name = $"Credit refund for {sub.Customer.GetPrimaryIdentity()}",
            Amount = amount,
            Currency = sub.Plan.Currency,
            AutoApproveClaims = autoApprove
        });
        subCtx.AddEvent(new SubscriptionEvent.CreditRefunded(sub, amount, sub.Plan.Currency, pullPaymentId, requestBaseUrl));
        return pullPaymentId;
    }

    private static HashSet<string> GetActiveMemberChangedPlans(SubscriptionContext subCtx)
    {
        HashSet<string> plansToUpdate = new();
        foreach (var evt in subCtx.Events.OfType<SubscriptionEvent.SubscriberEvent>())
        {
            if (evt is SubscriptionEvent.PlanStarted ps)
            {
                plansToUpdate.Add(ps.PreviousPlan.Id);
                plansToUpdate.Add(ps.Subscriber.PlanId);
            }
            else if (evt is SubscriptionEvent.SubscriberDisabled
                     or SubscriptionEvent.SubscriberActivated
                     or SubscriptionEvent.NewSubscriber)
            {
                plansToUpdate.Add(evt.Subscriber.PlanId);
            }
            else if (evt is SubscriptionEvent.SubscriberPhaseChanged pc)
            {
                var expired = pc.PreviousPhase is PhaseTypes.Expired;
                var newExpired = pc.Subscriber.Phase is PhaseTypes.Expired;
                var wasTrial = pc.PreviousPhase is PhaseTypes.Trial;
                var isTrial = pc.Subscriber.Phase is PhaseTypes.Trial;
                if (expired != newExpired || wasTrial != isTrial)
                    plansToUpdate.Add(evt.Subscriber.PlanId);
            }
        }
        return plansToUpdate;
    }

    public Task ProceedToSubscribe(string checkoutId, CancellationToken cancellationToken)
    => RunEvent(new SubscribeRequest(checkoutId), cancellationToken);

    private async Task StartPlanCheckoutWithoutInvoice(SubscriptionContext subCtx, PlanCheckoutData checkout)
    {
        var ctx = subCtx.Context;
        var sub = checkout.Subscriber;
        if (sub is null && !checkout.NewSubscriber)
            throw new InvalidOperationException("Bug: Subscriber is null and not a new subscriber");

        if (sub is null)
        {
            sub = await CreateSubscription(subCtx, checkout, false);
            if (sub is null)
                return;
        }

        await TryStartPlan(subCtx, checkout, sub);

        await ctx.SaveChangesAsync();
        await UpdateSubscriptionStates(subCtx, sub.Id);
    }

    private async Task ProcessSubscriptionPayment(InvoiceEntity invoice, string checkoutId, CancellationToken cancellationToken = default)
    {
        await using var ctx = applicationDbContextFactory.CreateContext();
        await ctx.Database.CreateExecutionStrategy().ExecuteAsync(
            token => ProcessSubscriptionPaymentCore(invoice, checkoutId, token), cancellationToken);
    }

    private async Task ProcessSubscriptionPaymentCore(InvoiceEntity invoice, string checkoutId, CancellationToken cancellationToken)
    {
        await using var subCtx = CreateContext(cancellationToken);
        var ctx = subCtx.Context;
        await using var tx = await ctx.Database.BeginTransactionAsync(cancellationToken);
        var lockedCheckoutId = await ctx.Database.GetDbConnection().ExecuteScalarAsync<string?>(new CommandDefinition(
            "SELECT id FROM subs_plan_checkouts WHERE id = @checkoutId FOR UPDATE",
            new { checkoutId }, tx.GetDbTransaction(), cancellationToken: cancellationToken));
        if (lockedCheckoutId is null)
            return;

        var checkout = await ctx.PlanCheckouts.GetCheckout(checkoutId);
        if (checkout?.Invoice is null || checkout.InvoiceId != invoice.Id ||
            checkout.Invoice.Status != invoice.Status.ToString() ||
            checkout.Plan.Offering.App.StoreDataId != invoice.StoreId)
            return;

        if (checkout.Subscriber is null && !checkout.NewSubscriber)
            throw new InvalidOperationException("Bug: Subscriber is null and not a new subscriber");

        try
        {
            await ReconcileCheckoutInvoice(subCtx, checkout, invoice);
            await tx.CommitAsync(cancellationToken);
        }
        catch
        {
            subCtx.DiscardEvents();
            throw;
        }
    }

    private async Task ReconcileCheckoutInvoice(SubscriptionContext subCtx, PlanCheckoutData checkout, InvoiceEntity invoice)
    {
        var ctx = subCtx.Context;
        var processing = invoice.Status == InvoiceStatus.Processing;
        var failed = invoice.Status is InvoiceStatus.Invalid or InvoiceStatus.Expired;
        var sub = checkout.Subscriber;
        if (sub is null && !failed && (!processing || checkout.Plan.OptimisticActivation))
        {
            sub = await CreateSubscription(subCtx, checkout, processing);
            if (sub is not null)
                ctx.SubscribersInvoices.Add(new SubscriberInvoiceData { SubscriberId = sub.Id, InvoiceId = invoice.Id });
        }

        if (sub is null)
            return;

        if (processing && sub.ProcessingInvoiceId is null)
            sub.ProcessingInvoiceId = invoice.Id;
        if (!processing && sub.ProcessingInvoiceId == invoice.Id)
        {
            sub.ProcessingInvoiceId = null;
            if (!failed)
                sub.OptimisticActivation = false;
        }

        if (processing)
        {
            if (checkout.Plan.OptimisticActivation && checkout.CreditPurchase is null)
                await TryStartPlan(subCtx, checkout, sub, invoice);
            await ctx.SaveChangesAsync(subCtx.CancellationToken);
            if (checkout.PlanStarted)
                await UpdateSubscriptionStates(subCtx, sub.Id);
            return;
        }

        if (failed && checkout.PlanStarted && checkout.CreditedByInvoice > 0m)
        {
            sub.IsSuspended = true;
            sub.SuspensionReason = "The plan has been started by an invoice which later became invalid.";
        }

        var invoiceCredit = failed ? 0m : subCtx.GetAmountToCredit(invoice);
        var diff = invoiceCredit - checkout.CreditedByInvoice;
        if (diff > 0m)
        {
            await subCtx.CreditSubscriber(sub, $"Credit purchase (Inv: {invoice.Id})", diff, invoice.Currency);
            checkout.CreditedByInvoice = invoiceCredit;
            await TryStartPlan(subCtx, checkout, sub);
        }
        else if (diff < 0m)
        {
            var description = failed ? $"Reversal (Inv: {invoice.Id})" : $"Adjustment (Inv: {invoice.Id})";
            if (await subCtx.TryChargeSubscriber(sub, description, -diff, allowOverdraft: true, currency: invoice.Currency))
                checkout.CreditedByInvoice = invoiceCredit;
            else
                Logs.PayServer.LogWarning("Subscriber credit adjustment for invoice {InvoiceId} exceeds balance limits", invoice.Id);
        }

        await ctx.SaveChangesAsync(subCtx.CancellationToken);
        if (!failed || diff != 0m)
            await UpdateSubscriptionStates(subCtx, sub.Id);
    }

    private async Task TryStartPlan(SubscriptionContext subCtx, PlanCheckoutData checkout, SubscriberData sub, InvoiceEntity? unconfirmedInvoice = null)
    {
        if (checkout.PlanStarted || !checkout.Plan.Currency.Equals(sub.Plan.Currency, StringComparison.OrdinalIgnoreCase))
            return;
        var prevPlan = sub.Plan;
        var now = subCtx.Now;
        using var scope = sub.NewPlanScope(checkout.Plan);
        if (sub.CanStartNextPlanEx(checkout.NewSubscriber))
        {
            if (checkout.IsTrial || await TryChargeForPlan($"Starting plan '{sub.NextPlan.Name}'", 0m))
            {
                sub.StartNextPlan(now, checkout.IsTrial);
                scope.Commit();
                checkout.PlanStarted = true;
            }
        }
        // In hard migrations, we stop the current plan by reimbursing what has
        // not yet been spent. We start the new plan.
        else if (checkout.OnPay == PlanCheckoutData.OnPayBehavior.HardMigration)
        {
            var unusedAmount = subCtx.RoundAmount(sub.GetUnusedPeriodAmount(now) ?? 0.0m, sub.Plan.Currency);
            var planChanges = await subCtx.Context.PlanChanges
                .Where(p => p.PlanId == sub.Plan.Id).ToListAsync();
            var planChange = planChanges
                .Where(p => p.PlanChangeId == sub.NewPlan?.Id)
                .Select(p => p.Type)
                .FirstOrDefault();
            var description = planChange switch
            {
                PlanChangeData.ChangeType.Upgrade => $"Upgrade to new plan '{sub.NewPlan?.Name}'",
                PlanChangeData.ChangeType.Downgrade => $"Downgrade to new plan '{sub.NewPlan?.Name}'",
                _ => $"Migration to plan '{sub.NewPlan?.Name}'"
            };
            if (checkout.IsTrial || await TryChargeForPlan(description, unusedAmount))
            {
                checkout.RefundAmount = unusedAmount;
                sub.StartNextPlan(now, checkout.IsTrial);
                scope.Commit();
                checkout.PlanStarted = true;
            }
        }
        if (checkout.PlanStarted)
            subCtx.AddEvent(new SubscriptionEvent.PlanStarted(sub, prevPlan));

        async Task<bool> TryChargeForPlan(string description, decimal refund)
        {
            var plan = sub.NextPlan;
            if (unconfirmedInvoice is null)
                return await subCtx.TryCreditDebitSubscriber(sub, description, refund, plan.Price) is not null;
            if (!plan.Currency.Equals(unconfirmedInvoice.Currency, StringComparison.OrdinalIgnoreCase))
                return false;

            var maxProvisionalCredit = Math.Max(0m, subCtx.RoundAmount(plan.Price, plan.Currency) - refund);
            var availablePayment = subCtx.GetAmountToCredit(unconfirmedInvoice);
            var credit = Math.Min(maxProvisionalCredit, availablePayment) - checkout.CreditedByInvoice;
            if (credit <= 0m)
                return await subCtx.TryCreditDebitSubscriber(sub, description, refund, plan.Price) is not null;

            var tx = subCtx.Context.Database.CurrentTransaction!;
            var cancellationToken = subCtx.CancellationToken;
            var eventCount = subCtx.Events.Count;
            const string savepoint = "plan_purchase";
            await tx.CreateSavepointAsync(savepoint, cancellationToken);
            if (await subCtx.TryCreditDebitSubscriber(sub, $"Credit purchase (Inv: {unconfirmedInvoice.Id})", credit, 0m, currency: unconfirmedInvoice.Currency) is not null &&
                await subCtx.TryCreditDebitSubscriber(sub, description, refund, plan.Price) is not null)
            {
                checkout.CreditedByInvoice += credit;
                await tx.ReleaseSavepointAsync(savepoint, cancellationToken);
                return true;
            }

            await tx.RollbackToSavepointAsync(savepoint, cancellationToken);
            subCtx.DiscardEvents(eventCount);
            await subCtx.ReloadCredits(sub);
            return false;
        }
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
            time = (subscriber.GracePeriodEnd ?? subscriber.PeriodEnd)!.Value - DateTimeOffset.UtcNow;
        else
            throw new InvalidOperationException("Invalid phase");

        await this.MoveTime(selector, time);
    }

    private async Task<SubscriberData?> CreateSubscription(SubscriptionContext subCtx, PlanCheckoutData checkout, bool optimisticActivation)
    {
        var ctx = subCtx.Context;
        var plan = checkout.Plan;

        var email = checkout.GetEmail();
        if (email is null)
            return null;
        var cust = await ctx.Customers.GetOrUpdate(checkout.Plan.Offering.App.StoreDataId, CustomerSelector.ByEmail(email));
        var (sub, created) =
            await ctx.Subscribers.GetOrCreateByCustomerId(cust.Id, plan.OfferingId, plan.Id, optimisticActivation, checkout.TestAccount,
                JsonParser.ParseObject(checkout.NewSubscriberMetadata));
        if (!created || sub is null)
            return null;
        checkout.Subscriber = sub;
        checkout.SubscriberId = sub.Id;
        await ctx.SaveChangesAsync();
        subCtx.AddEvent(new SubscriptionEvent.NewSubscriber(sub, checkout.BaseUrl));
        return sub;
    }

    public static async Task UpdatePlanStats(ApplicationDbContext ctx, string planId)
    {
        await ctx.Database.GetDbConnection()
            .ExecuteAsync("""
                          WITH defaults AS (SELECT 0),
                              stats AS (
                              SELECT COUNT(1) AS subscribers_count,
                                     SUM( CASE sp.recurring_type
                                                                        WHEN 'Monthly' THEN sp.price
                                                                        WHEN 'Quarterly' THEN sp.price / 3.0::numeric
                                                                        WHEN 'Yearly' THEN sp.price / 12.0::numeric
                                                                        WHEN 'Lifetime' THEN 0
                                                                      END) FILTER (WHERE ss.phase != 'Trial') AS monthly_revenue
                              FROM subs_subscribers ss
                              JOIN subs_plans sp ON ss.plan_id = sp.id
                              WHERE ss.plan_id = @id AND ss.active
                              GROUP BY ss.plan_id
                          )
                          UPDATE subs_plans AS sp
                          SET members_count = COALESCE(stats.subscribers_count,0),
                            monthly_revenue = COALESCE(stats.monthly_revenue, 0)
                          FROM defaults d -- forces one row
                          LEFT JOIN stats ON TRUE
                          WHERE sp.id = @id;
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
    public Task Unsuspend(long subId)
        => RunEvent(new SuspendRequest(subId, null, false));

    public class UpdateCreditParameters
    {
        public long SubscriberId { get; set; }
        public string? Description { get; set; }
        public bool AllowOverdraft { get; set; }
        public decimal Credit { get; set; }
        public decimal Charge { get; set; }
        public string? Currency { get; set; }
    }

    public async Task<decimal?> UpdateCredit(UpdateCreditParameters parameters)
    {
        await using var subCtx = CreateContext();
        var sub = await subCtx.Context.Subscribers.GetById(parameters.SubscriberId);
        if (sub is null) return null;
        return await subCtx.TryCreditDebitSubscriber(sub,
            parameters.Description ?? "No description",
            parameters.Credit,
            parameters.Charge,
            parameters.AllowOverdraft,
            parameters.Currency);
    }


    public async Task<string?> CreateCreditCheckout(string portalSessionId, decimal? value)
    {
        await using var subCtx = CreateContext(CancellationToken);
        var ctx = subCtx.Context;
        var portal = await ctx.PortalSessions.GetActiveById(portalSessionId);
        if (portal is null)
            return null;

        if (value is not null)
            value = subCtx.RoundAmount(value.Value, portal.Subscriber.Plan.Currency);
        if (value is <= 0.0m)
            return null;

        var checkout = new PlanCheckoutData(portal.Subscriber)
        {
            SuccessRedirectUrl = linkGenerator.SubscriberPortalLink(portalSessionId, portal.BaseUrl),
            CreditPurchase = value,
            BaseUrl = portal.BaseUrl
        };
        ctx.PlanCheckouts.Add(checkout);
        await ctx.SaveChangesAsync();
        await this.CreateInvoiceForCheckout(subCtx, checkout);
        return checkout.InvoiceId;
    }

    public async Task<PlanMigrationResult> CreatePlanMigrationCheckout(string portalSessionId, string? planId, PlanCheckoutData.OnPayBehavior migrationType,
        RequestBaseUrl requestBaseUrl)
    {
        await using var ctx = applicationDbContextFactory.CreateContext();
        var portal = await ctx.PortalSessions.GetActiveById(portalSessionId);
        if (portal is null)
            return new PlanMigrationResult.NotFound();

        PlanData? plan = null;
        if (planId is not null)
        {
            plan = await ctx.Plans.GetPlanFromId(planId, portal.Subscriber.OfferingId);
            if (plan is null)
                return new PlanMigrationResult.NotFound();

            var planChangeRecord  = portal.Subscriber.Plan.PlanChanges
                .FirstOrDefault(pc => pc.PlanId == portal.Subscriber.PlanId && pc.PlanChangeId == planId);
            if (planChangeRecord == null)
                return new PlanMigrationResult.NotFound();

            if (!plan.Currency.Equals(portal.Subscriber.Plan.Currency, StringComparison.OrdinalIgnoreCase))
                return new PlanMigrationResult.NotFound();

            if (planChangeRecord.Timing == PlanChangeData.ChangeTiming.AtPeriodEnd)
            {
                if (portal.Subscriber.PeriodEnd is not null && portal.Subscriber.PeriodEnd > DateTimeOffset.UtcNow
                    && portal.Subscriber.Phase == PhaseTypes.Normal)
                {
                    portal.Subscriber.NewPlanId = planId;
                    portal.Subscriber.NewPlan = plan;
                    await ctx.SaveChangesAsync();
                    return new PlanMigrationResult.Scheduled();
                }
            }
        }

        var checkout = new PlanCheckoutData(portal.Subscriber, plan)
        {
            SuccessRedirectUrl = linkGenerator.SubscriberPortalLink(portalSessionId, requestBaseUrl),
            OnPay = migrationType,
            BaseUrl = requestBaseUrl,
        };
        ctx.PlanCheckouts.Add(checkout);
        await ctx.SaveChangesAsync();
        return new PlanMigrationResult.Checkout(checkout.Id);
    }

    public abstract record PlanMigrationResult
    {
        public record Checkout(string CheckoutId) : PlanMigrationResult;
        public record Scheduled : PlanMigrationResult;
        public record NotFound : PlanMigrationResult;
    }
}
