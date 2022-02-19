using System.Threading.Tasks;
using BTCPayServer.Services.Custodian.Client.Kraken;
using Xunit;
using Xunit.Abstractions;

namespace BTCPayServer.Tests.Custodians;

[Collection(nameof(NonParallelizableCollectionDefinition))]
public class KrakenTests : UnitTestBase
{
    public const int TestTimeout = TestUtils.TestTimeout;

    public KrakenTests(ITestOutputHelper helper) : base(helper)
    {
    }

    [Fact(Timeout = TestTimeout)]
    [Trait("Integration", "Integration")]
    public async Task KrakenExchangeTests()
    {
        using var tester = CreateServerTester();
        await tester.StartAsync();
        //var languageService = tester.PayTester.GetService<LanguageService>();
        
        var kraken = tester.PayTester.GetService<KrakenExchange>();
        
        Assert.NotEmpty(kraken.GetCode());
        Assert.NotEmpty(kraken.GetName());
        
        // TODO Test: Kraken request signing / hash
        // TODO make these tests generic so we can test any custodian we want. Split out into ICustodian, ICanTrade, ICanDeposit and ICanWithdraw

        // TODO Test: kraken.WithdrawAsync();
        // TODO Test: kraken.TradeMarketAsync();
        // TODO Test: kraken.GetAssetBalancesAsync();
        // TODO Test: kraken.GetDepositablePaymentMethods();
        // TODO Test: kraken.GetDepositAddressAsync();
        // TODO Test: kraken.GetTradableAssetPairs();
        // TODO Test: kraken.GetTradeInfoAsync();
        // TODO Test: kraken.GetWithdrawablePaymentMethods();
        // TODO Test: kraken.GetWithdrawalInfoAsync();
        // TODO Test: kraken.GetQuoteForAssetAsync();
        

    }
}
