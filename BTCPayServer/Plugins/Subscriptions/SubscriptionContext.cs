#nullable  enable
using System;
using System.Collections.Generic;
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
    public CancellationToken CancellationToken { get; set; } = cancellationToken;
    public DateTimeOffset Now { get; set; } = DateTimeOffset.UtcNow;
    public ApplicationDbContext Context => ctx;
    public void AddEvent(object evt) => _evts.Add(evt);
    public IReadOnlyList<object> Events => _evts;

    public decimal RoundAmount(decimal amount, string currency)
        => Math.Round(amount, currencyNameTable.GetNumberFormatInfo(currency)?.CurrencyDecimalDigits ?? 2);


    public decimal GetAmountToCredit(InvoiceEntity invoice)
        // If the subscriber settled more than expected, we credit the subscriber with the difference.
        => RoundAmount(invoice.Status is InvoiceStatus.Processing ? invoice.PaidAmount.Net : invoice.NetSettled, invoice.Currency);

    public async Task<decimal> CreditSubscriber(SubscriberData sub, decimal credit)
    => (await TryCreditChargeSubscriber(sub, credit, 0m, true))!.Value;

    public async Task<bool> TryChargeSubscriber(SubscriberData sub, decimal charge, bool force = false)
    => (await TryCreditChargeSubscriber(sub, 0m, charge, force)) is not null;

    private static async Task<decimal?> UpdateCredit(SubscriberData sub, decimal diff, bool force, ApplicationDbContext ctx)
    {
        if (diff >= 0)
            force = true;
        var amountCondition = force ? "1=1" : "subscriptions_subscriber_credits.amount >= -@diff";

        var amount = await ctx.Database.GetDbConnection()
            .ExecuteScalarAsync<decimal?>($"""
                                           INSERT INTO subscriptions_subscriber_credits (subscriber_id, currency, amount)
                                           VALUES (@id, @currency, @diff)
                                           ON CONFLICT (subscriber_id, currency)
                                           DO UPDATE
                                               SET amount = subscriptions_subscriber_credits.amount + EXCLUDED.amount
                                               WHERE {amountCondition}
                                           RETURNING amount
                                           """, new { id = sub.Id, currency = sub.Plan.Currency, diff });
        foreach (var c in sub.Credits)
            ctx.Entry(c).State = EntityState.Detached;
        sub.Credits.Clear();
        await ctx.Entry(sub).Collection(c => c.Credits).Query().LoadAsync();
        return amount;
    }

    public async Task<decimal?> TryCreditChargeSubscriber(SubscriberData sub, decimal credit, decimal charge, bool force = false)
    {
        if (credit < 0)
            throw new ArgumentOutOfRangeException(nameof(credit), "Credit must be positive");
        if (charge < 0)
            throw new ArgumentOutOfRangeException(nameof(charge), "Charge must be positive");
        var amount = await UpdateCredit(sub, credit - charge, force, ctx);
        if (amount is { } newTotal)
        {
            if (credit != 0)
                AddEvent(new SubscriptionEvent.SubscriberCredited(sub, newTotal + charge, credit, sub.Plan.Currency));
            if (charge != 0)
                AddEvent(new SubscriptionEvent.SubscriberCharged(sub, newTotal, charge, sub.Plan.Currency));
        }
        return amount;
    }

    public async ValueTask DisposeAsync()
    {
        foreach (var evt in _evts)
            aggregator.Publish(evt, evt.GetType());
        await ctx.DisposeAsync();
    }
}
