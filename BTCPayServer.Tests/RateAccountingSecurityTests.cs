using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Logging;
using BTCPayServer.Payments;
using BTCPayServer.Payments.Bitcoin;
using BTCPayServer.Payouts;
using BTCPayServer.Rating;
using BTCPayServer.Services.Invoices;
using BTCPayServer.Services.Rates;
using Microsoft.AspNetCore.Mvc;
using NBitcoin;
using NBXplorer;
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

    [Fact]
    public async Task PayoutApprovalRateIgnoresStoreSpread()
    {
        var network = new BTCPayNetwork
        {
            CryptoCode = "BTC",
            NBXplorerNetwork = new NBXplorerNetworkProvider(ChainName.Regtest).GetFromCryptoCode("BTC")
        };
        var handlers = new PayoutMethodHandlerDictionary([new TestPayoutHandler(network)]);
        var store = new StoreData { Id = "store" };
        store.SetStoreBlob(new StoreBlob
        {
            Spread = 0.5m,
            PrimaryRateSettings = new StoreBlob.RateSettings
            {
                RateScripting = true,
                RateScript = "BTC_USD = 100;"
            }
        });
        var payout = new Data.PayoutData
        {
            StoreDataId = store.Id,
            StoreData = store,
            PayoutMethodId = PayoutTypes.CHAIN.GetPayoutMethodId("BTC").ToString(),
            PullPaymentData = new Data.PullPaymentData { Currency = "USD" }
        };
        var service = new PullPaymentHostedService(null, null, null, null, handlers,
            new DefaultRulesCollection([new DefaultRules("X_X = kraken(X_X);")]), null,
            new RateFetcher(null!), null, new Logs(), null, null, null);

        var rate = await service.GetRate(payout, null, CancellationToken.None);

        Assert.Equal(100m, rate.BidAsk.Ask);
    }

    private sealed class TestPayoutHandler(BTCPayNetwork network) : IPayoutHandler, IHasNetwork
    {
        public BTCPayNetwork Network { get; } = network;
        public string Currency => Network.CryptoCode;
        public PayoutMethodId PayoutMethodId { get; } = PayoutTypes.CHAIN.GetPayoutMethodId(network.CryptoCode);
        public bool IsSupported(StoreData storeData) => true;
        public Task TrackClaim(ClaimRequest claimRequest, Data.PayoutData payoutData) => throw new NotImplementedException();
        public Task<(IClaimDestination destination, string error)> ParseClaimDestination(string destination, CancellationToken cancellationToken) => throw new NotImplementedException();
        public (bool valid, string error) ValidateClaimDestination(IClaimDestination claimDestination, PullPaymentBlob pullPaymentBlob) => throw new NotImplementedException();
        public IPayoutProof ParseProof(Data.PayoutData payout) => throw new NotImplementedException();
        public void StartBackgroundCheck(Action<Type[]> subscribe) => throw new NotImplementedException();
        public Task BackgroundCheck(object o) => throw new NotImplementedException();
        public Task<decimal> GetMinimumPayoutAmount(IClaimDestination claimDestination) => throw new NotImplementedException();
        public Dictionary<Client.Models.PayoutState, List<(string Action, string Text)>> GetPayoutSpecificActions() => throw new NotImplementedException();
        public Task<StatusMessageModel> DoSpecificAction(string action, string[] payoutIds, string storeId) => throw new NotImplementedException();
        public Task<IActionResult> InitiatePayment(string[] payoutIds) => throw new NotImplementedException();
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
