using System;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Data.Subscriptions;
using BTCPayServer.Plugins.Subscriptions;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;

namespace BTCPayServer.Tests;

[Trait("Fast", "Fast")]
public class SubscriptionSecurityTests
{
    [Fact]
    public void UnusedPeriodAmountCannotExceedPaidAmount()
    {
        var now = DateTimeOffset.UtcNow;
        var subscriber = new SubscriberData
        {
            PlanStarted = now.AddMinutes(1),
            PeriodEnd = now.AddDays(15),
            PaidAmount = 10m
        };

        var unused = subscriber.GetUnusedPeriodAmount(now);

        Assert.NotNull(unused);
        Assert.InRange(unused.Value, 0m, subscriber.PaidAmount!.Value);
    }

    [Fact]
    public void RenewalPaidDuringGracePeriodDoesNotRefundGraceDays()
    {
        var now = new DateTimeOffset(2026, 10, 22, 0, 0, 0, TimeSpan.Zero);
        var periodEnd = now.AddDays(-14);
        var plan = new PlanData
        {
            Price = 31m,
            Currency = "USD",
            RecurringType = PlanData.RecurringInterval.Monthly,
            GracePeriodDays = 15,
            Offering = new OfferingData { DefaultPaymentRemindersDays = 3 }
        };
        var subscriber = new SubscriberData
        {
            Plan = plan,
            PlanStarted = periodEnd.AddMonths(-1),
            PeriodEnd = periodEnd,
            GracePeriodEnd = periodEnd.AddDays(plan.GracePeriodDays),
            PaidAmount = plan.Price
        };
        Assert.Equal(SubscriberData.PhaseTypes.Grace, subscriber.GetExpectedPhase(now));

        subscriber.StartNextPlan(now);

        Assert.Equal(periodEnd, subscriber.PlanStarted);
        Assert.Equal(periodEnd.AddMonths(1), subscriber.PeriodEnd);
        Assert.Equal(17m, subscriber.GetUnusedPeriodAmount(now)!.Value, 2);

        now = subscriber.GracePeriodEnd!.Value;
        Assert.Equal(SubscriberData.PhaseTypes.Expired, subscriber.GetExpectedPhase(now));
        subscriber.StartNextPlan(now);
        Assert.Equal(now, subscriber.PlanStarted);
        Assert.Equal(plan.Price, subscriber.GetUnusedPeriodAmount(now));
    }

    [Theory]
    [InlineData(0.0051, 0.0)]
    [InlineData(0.01, 0.01)]
    [InlineData(0.0151, 0.01)]
    [InlineData(10.00000002, 10.0)]
    public void InvoiceCreditCannotExceedPaidAmount(decimal paid, decimal expectedCredit)
    {
        var subCtx = new SubscriptionContext(null!, null!, new CurrencyNameTable([], NullLogger<CurrencyNameTable>.Instance), default);
        foreach (var status in new[] { InvoiceStatus.Processing, InvoiceStatus.Settled })
        {
            var invoice = new InvoiceEntity { Currency = "USD", Price = paid, Status = status };
            invoice.AddRate(new("BTC", "USD"), 100_000m);
#pragma warning disable CS0618
            invoice.Payments = [new PaymentEntity
            {
                Currency = "BTC",
                Value = paid / 100_000m,
                Status = status == InvoiceStatus.Processing ? PaymentStatus.Processing : PaymentStatus.Settled
            }];
#pragma warning restore CS0618
            invoice.UpdateTotals();

            var credit = subCtx.GetAmountToCredit(invoice);
            Assert.Equal(paid, invoice.PaidAmount.Net);
            Assert.Equal(expectedCredit, credit);
            Assert.True(credit <= paid);
        }
    }
}
