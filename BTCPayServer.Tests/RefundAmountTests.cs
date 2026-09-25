using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Payments;
using BTCPayServer.Services.Invoices;
using Xunit;

namespace BTCPayServer.Tests;

public class RefundAmountTests(ITestOutputHelper helper) : UnitTestBase(helper)
{
    [Fact]
    [Trait("Fast", "Fast")]
    public void PaidSettledExcludesProcessingPayments()
    {
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("BTC");
        var entity = new InvoiceEntity
        {
            Currency = "USD",
            Price = 5000m
        };
#pragma warning disable CS0618
        entity.Payments = new List<PaymentEntity>();
        entity.Rates["BTC"] = 5000m;
#pragma warning restore CS0618
        entity.SetPaymentPrompt(pmi, new PaymentPrompt
        {
            Currency = "BTC",
            Divisibility = 8
        });

#pragma warning disable CS0618
        entity.Payments.Add(new PaymentEntity
        {
            Currency = "BTC",
            Value = 0.4m,
            Status = PaymentStatus.Settled
        });
        entity.Payments.Add(new PaymentEntity
        {
            Currency = "BTC",
            Value = 0.6m,
            Status = PaymentStatus.Processing
        });
#pragma warning restore CS0618

        entity.UpdateTotals();
        var accounting = entity.GetPaymentPrompts().TryGet(pmi).Calculate();

        Assert.Equal(1.0m, accounting.Paid);
        Assert.Equal(0.4m, accounting.PaidSettled);

        // Recalculating totals must not accumulate the settled amount again.
        entity.UpdateTotals();
        Assert.Equal(0.4m, entity.GetPaymentPrompts().TryGet(pmi).Calculate().PaidSettled);
    }

    [Fact]
    [Trait("Fast", "Fast")]
    public void PaidSettledMatchesPaidWhenAllPaymentsAreSettled()
    {
        var pmi = PaymentTypes.CHAIN.GetPaymentMethodId("BTC");
        var entity = new InvoiceEntity
        {
            Currency = "USD",
            Price = 5000m
        };
#pragma warning disable CS0618
        entity.Payments = new List<PaymentEntity>();
        entity.Rates["BTC"] = 5000m;
        entity.Payments.Add(new PaymentEntity
        {
            Currency = "BTC",
            Value = 1.0m,
            Status = PaymentStatus.Settled
        });
#pragma warning restore CS0618
        entity.SetPaymentPrompt(pmi, new PaymentPrompt
        {
            Currency = "BTC",
            Divisibility = 8
        });

        entity.UpdateTotals();
        var accounting = entity.GetPaymentPrompts().TryGet(pmi).Calculate();

        Assert.Equal(accounting.Paid, accounting.PaidSettled);
        Assert.Equal(1.0m, accounting.PaidSettled);
    }
}
