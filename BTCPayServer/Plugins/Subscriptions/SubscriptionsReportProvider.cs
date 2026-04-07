using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services;
using BTCPayServer.Services.Reporting;
using Microsoft.EntityFrameworkCore;

namespace BTCPayServer.Plugins.Subscriptions;

public class SubscriptionsReportProvider(ApplicationDbContextFactory dbContextFactory, DisplayFormatter displayFormatter) : ReportProvider
{
    public override string Name => "Subscriptions";

    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        queryContext.ViewDefinition = new ViewDefinition
        {
            Fields = new List<StoreReportResponse.Field>
            {
                new("Created", "datetime"),
                new("Offering", "text"),
                new("Plan", "text"),
                new("Email", "text"),
                new("Phase", "text"),
                new("Active", "boolean"),
                new("CreditBalance", "amount"),
                new("NextBilling", "datetime"),
            },
            Charts =
            {
                new()
                {
                    Name = "Active subscribers by plan",
                    Groups = { "Plan" },
                    Aggregates = { "CreditBalance" },
                    HasGrandTotal = true
                }
            }
        };

        await using var ctx = dbContextFactory.CreateContext();
        var subscribers = await ctx.Subscribers
            .Include(s => s.Plan).ThenInclude(p => p.Offering).ThenInclude(o => o.App)
            .Include(s => s.Customer).ThenInclude(c => c.CustomerIdentities)
            .Include(s => s.Credits)
            .Where(s => s.Plan.Offering.App.StoreDataId == queryContext.StoreId)
            .Where(s => s.CreatedAt >= queryContext.From && s.CreatedAt <= queryContext.To)
            .ToListAsync(cancellation);

        foreach (var sub in subscribers)
        {
            var data = queryContext.AddData();
            data.Add(sub.CreatedAt);
            data.Add(sub.Plan.Offering.App.Name);
            data.Add(sub.Plan.Name);
            data.Add(sub.Customer.GetPrimaryIdentity());
            data.Add(sub.Phase.ToString());
            data.Add(sub.IsActive);
            data.Add(displayFormatter.ToFormattedAmount(sub.GetCredit(), sub.Plan.Currency));
            data.Add(sub.NextPaymentDue);
        }
    }
}

public class SubscriptionTransactionsReportProvider(ApplicationDbContextFactory dbContextFactory, DisplayFormatter displayFormatter) : ReportProvider
{
    public override string Name => "Subscription Transactions";

    public override async Task Query(QueryContext queryContext, CancellationToken cancellation)
    {
        queryContext.ViewDefinition = new ViewDefinition
        {
            Fields = new List<StoreReportResponse.Field>
            {
                new("Date", "datetime"),
                new("Email", "text"),
                new("Offering", "text"),
                new("Plan", "text"),
                new("Description", "text"),
                new("Debit", "amount"),
                new("Credit", "amount"),
                new("Balance", "amount"),
            },
            Charts =
            {
                new()
                {
                    Name = "Revenue by plan",
                    Groups = { "Plan" },
                    Aggregates = { "Debit" },
                    HasGrandTotal = true
                }
            }
        };

        await using var ctx = dbContextFactory.CreateContext();
        var history = await ctx.SubscriberCreditHistory
            .Include(h => h.SubscriberCredit).ThenInclude(c => c.Subscriber).ThenInclude(s => s.Plan).ThenInclude(p => p.Offering).ThenInclude(o => o.App)
            .Include(h => h.SubscriberCredit).ThenInclude(c => c.Subscriber).ThenInclude(s => s.Customer).ThenInclude(c => c.CustomerIdentities)
            .Where(h => h.SubscriberCredit.Subscriber.Plan.Offering.App.StoreDataId == queryContext.StoreId)
            .Where(h => h.CreatedAt >= queryContext.From && h.CreatedAt <= queryContext.To)
            .OrderBy(h => h.CreatedAt).ToListAsync(cancellation);

        foreach (var entry in history)
        {
            var sub = entry.SubscriberCredit.Subscriber;
            var currency = entry.Currency;
            var data = queryContext.AddData();
            data.Add(entry.CreatedAt);
            data.Add(sub.Customer.GetPrimaryIdentity());
            data.Add(sub.Plan.Offering.App.Name);
            data.Add(sub.Plan.Name);
            data.Add(entry.Description);
            data.Add(entry.Debit > 0 ? displayFormatter.ToFormattedAmount(entry.Debit, currency) : null);
            data.Add(entry.Credit > 0 ? displayFormatter.ToFormattedAmount(entry.Credit, currency) : null);
            data.Add(displayFormatter.ToFormattedAmount(entry.Balance, currency));
        }
    }
}
