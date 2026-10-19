using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Data;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Xunit;

namespace BTCPayServer.Tests;

[Trait("Fast", "Fast")]
public class RateAccountingSecurityTests
{
    [Fact]
    public void CurrencyPairTextCannotInjectAnInverseRateExpression()
    {
        Assert.True(RateRules.TryParse("X_USD = 10;", out var rules));

        var rule = rules.GetRuleFor(new CurrencyPair("USD", "2;IGNORED"));

        Assert.False(rule.Reevaluate());
        Assert.Null(rule.BidAsk);
    }

    [Fact]
    public void PreferredExchangeCannotInjectRateRules()
    {
        var defaultRules = new DefaultRulesCollection(new[]
        {
            new DefaultRules("X_X = kraken(X_X);")
        });

        var rule = defaultRules
            .WithPreferredExchange("kraken(X_X); BTC_USD = 1; //")
            .GetRuleFor(new CurrencyPair("BTC", "USD"));

        Assert.Equal("kraken(BTC_USD)", rule.ToString());
    }

    [Fact]
    public void InvoiceAccountingDoesNotOverflowOnTinyPositiveRates()
    {
        var entity = new InvoiceEntity
        {
            Currency = "USD",
            Price = 100m
        };
        var paymentMethodId = PaymentTypes.CHAIN.GetPaymentMethodId("BTC");
#pragma warning disable CS0618
        entity.Rates["BTC"] = 0.0000000000000000000000000001m;
#pragma warning restore CS0618
        entity.SetPaymentPrompt(paymentMethodId, new PaymentPrompt
        {
            Currency = "BTC",
            Divisibility = 8
        });
        entity.UpdateTotals();

        var accounting = entity.GetPaymentPrompt(paymentMethodId).Calculate();

        Assert.True(accounting.TotalDue > 0m);
    }

    [Fact]
    public async Task InvoiceCreationRejectsNegativeRates()
    {
        var handler = new TestPaymentMethodHandler();
        var handlers = new PaymentMethodHandlerDictionary([handler]);
        var store = new StoreData { Id = "store" };
        store.SetPaymentMethodConfig(handler, new object());
        var invoice = new InvoiceEntity
        {
            StoreId = store.Id,
            Currency = "USD",
            Price = 100m
        };
        var context = new InvoiceCreationContext(
            store, new StoreBlob(), invoice, new InvoiceLogs(), handlers, null, null!);

        await context.BeforeFetchingRates();
        await context.FetchingRates(
            new RateFetcher(null!),
            new RateRulesCollection(RateRules.Parse("BTC_USD = -1;"), null),
            CancellationToken.None);

        var paymentMethod = context.PaymentMethodContexts[handler.PaymentMethodId];
        Assert.Equal(PaymentMethodContext.ContextStatus.Failed, paymentMethod.Status);
        Assert.False(invoice.TryGetRate("BTC", out _));
    }

    private sealed class TestPaymentMethodHandler : IPaymentMethodHandler
    {
        public PaymentMethodId PaymentMethodId { get; } = PaymentMethodId.Parse("BTC-NEGATIVE");
        public JsonSerializer Serializer { get; } = JsonSerializer.CreateDefault();

        public Task BeforeFetchingRates(PaymentMethodContext context)
        {
            context.Prompt.Currency = "BTC";
            context.Prompt.Divisibility = 8;
            return Task.CompletedTask;
        }

        public Task ConfigurePrompt(PaymentMethodContext context) => Task.CompletedTask;
        public object ParsePaymentPromptDetails(JToken details) => details;
        public object ParsePaymentMethodConfig(JToken config) => config;
        public object ParsePaymentDetails(JToken details) => details;
    }
}
