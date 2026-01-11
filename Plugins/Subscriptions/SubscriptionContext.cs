#nullable  enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Events;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Dapper;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriptionContext(ApplicationDbContext ctx, EventAggregator aggregator, CurrencyNameTable currencyNameTable, CancellationToken cancellationToken) : IAsyncDisposable
{
    List<object> _evts = new List<object>();
    public CancellationToken CancellationToken { get; } = cancellationToken;
    public DateTimeOffset Now { get; } = DateTimeOffset.UtcNow;
    public ApplicationDbContext Context => ctx;
    public void AddEvent(object evt) => _evts.Add(evt);
    public IReadOnlyList<object> Events => _evts;

    public decimal RoundAmount(decimal amount, string currency)
        => Math.Round(amount, currencyNameTable.GetNumberFormatInfo(currency)?.CurrencyDecimalDigits ?? 2);


    public decimal GetAmountToCredit(InvoiceEntity invoice)
        // If the subscriber settled more than expected, we credit the subscriber with the difference.
        => RoundAmount(invoice.Status is InvoiceStatus.Processing ? invoice.PaidAmount.Net : invoice.NetSettled, invoice.Currency);

    public async Task<decimal> CreditSubscriber(SubscriberData sub, string description, decimal credit)
    => (await TryCreditDebitSubscriber(sub, description, credit, 0m, true))!.Value;

    public async Task<bool> TryChargeSubscriber(SubscriberData sub, string description, decimal charge, bool allowOverdraft = false)
    => (await TryCreditDebitSubscriber(sub, description, 0m, charge, allowOverdraft)) is not null;

    private static async Task<decimal?> UpdateCredit(BalanceTransaction tx, bool force, ApplicationDbContext ctx)
    {
        var diff = tx.Diff;
        if (diff >= 0)
            force = true;
        var amountCondition = force ? "1=1" : "new_amount >= 0";

        var amount = await ctx.Database.GetDbConnection()
            .ExecuteScalarAsync<decimal?>($"""
                                           WITH
                                           change AS (
                                                SELECT o.id, o.currency,  COALESCE(c.amount, 0) + o.diff AS new_amount
                                                FROM (SELECT @id id, @currency currency, @diff diff) AS o
                                                LEFT JOIN subs_subscriber_credits c ON c.subscriber_id = o.id AND c.currency = o.currency
                                           ),
                                           up AS (
                                               INSERT INTO subs_subscriber_credits AS c (subscriber_id, currency, amount)
                                               SELECT id, currency, new_amount FROM change
                                               WHERE {amountCondition}
                                               ON CONFLICT (subscriber_id, currency)
                                               DO UPDATE SET amount = EXCLUDED.amount
                                               RETURNING c.subscriber_id, c.currency, @diff AS diff, c.amount AS balance
                                           ),
                                           hist AS (
                                               INSERT INTO subs_subscriber_credits_history(subscriber_id, currency, description, debit, credit, balance)
                                               SELECT
                                                up.subscriber_id,
                                                up.currency,
                                                @desc,
                                                CASE WHEN up.diff < 0 THEN ABS(up.diff) ELSE 0 END AS debit,
                                                CASE WHEN up.diff > 0 THEN up.diff ELSE 0 END AS credit,
                                                up.balance
                                               FROM up
                                           )
                                           SELECT balance from up;
                                           """, new { id = tx.SubscriberId, currency = tx.Currency, diff, desc = tx.Description });
        return amount;
    }

    private static async Task ReloadCredits(SubscriberData sub, ApplicationDbContext ctx)
    {
        foreach (var c in sub.Credits)
            ctx.Entry(c).State = EntityState.Detached;
        sub.Credits.Clear();
        await ctx.Entry(sub).Collection(c => c.Credits).Query().LoadAsync();
    }

    public async Task<decimal?> TryCreditDebitSubscriber(SubscriberData sub, string description, decimal credit, decimal charge, bool allowOverdraft = false, string? currency = null)
    {
        currency ??= sub.Plan.Currency;
        currency = currency.ToUpperInvariant().Trim();
        charge = RoundAmount(charge, currency);
        credit = RoundAmount(credit, currency);
        var tx = new BalanceTransaction(sub.Id, currency, credit, charge, description);
        var amount = await UpdateCredit(tx, allowOverdraft, ctx);
        await ReloadCredits(sub, ctx);
        if (amount is { } newTotal)
        {
            if (tx.Credit != 0)
                AddEvent(new SubscriptionEvent.SubscriberCredited(sub, newTotal + tx.Debit, tx.Credit, currency));
            if (tx.Debit != 0)
                AddEvent(new SubscriptionEvent.SubscriberDebited(sub, newTotal, tx.Debit, currency));
        }
        return amount;
    }

    public async ValueTask DisposeAsync()
    {
        var plans =
            _evts.OfType<SubscriptionEvent.SubscriberEvent>()
                .SelectMany(s => s.Subscriber.Offering.Plans)
                .Where(p => !p.FeaturesLoaded)
                .ToList();
        await ctx.Plans.FetchPlanFeaturesAsync(plans);
        foreach (var evt in _evts)
        {
            aggregator.Publish(evt, evt.GetType());
        }

        await ctx.DisposeAsync();
    }
}
