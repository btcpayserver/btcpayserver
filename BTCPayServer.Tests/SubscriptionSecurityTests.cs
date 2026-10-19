using System;
using BTCPayServer.Data.Subscriptions;
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
}
