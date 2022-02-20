using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
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

        var cancellationToken = new CancellationToken();
        var kraken = tester.PayTester.GetService<KrakenExchange>();
        
        Assert.NotNull(kraken);
        Assert.NotNull(kraken.GetCode());
        Assert.NotNull(kraken.GetName());
        
        var pairs = kraken.GetTradableAssetPairs();
        Assert.NotNull(pairs);
        Assert.True(pairs.Count > 0);
        foreach (AssetPairData pair in pairs)
        {
            Assert.NotNull(pair.AssetBought);
            Assert.NotNull(pair.AssetSold);
            Assert.IsType<KrakenAssetPair>(pair);
            KrakenAssetPair krakenAssetPair = (KrakenAssetPair) pair;
            Assert.NotNull(krakenAssetPair.PairCode);
        }
        Assert.NotNull(pairs[0].AssetBought);
        
        // Test: GetDepositablePaymentMethods();
        var depositablePaymentMethods = kraken.GetDepositablePaymentMethods();
        Assert.NotNull(depositablePaymentMethods);
        Assert.True(depositablePaymentMethods.Length > 0);
        Assert.Contains("BTC-OnChain", depositablePaymentMethods);
        
        
        // Test: GetWithdrawablePaymentMethods();
        var withdrawablePaymentMethods = kraken.GetWithdrawablePaymentMethods();
        Assert.NotNull(withdrawablePaymentMethods);
        Assert.True(withdrawablePaymentMethods.Length > 0);
        Assert.Contains("BTC-OnChain", withdrawablePaymentMethods);
        
        
        // Test: GetQuoteForAssetAsync();
        var btcQuote = await kraken.GetQuoteForAssetAsync("USD","BTC", null, cancellationToken);
        Assert.NotNull(btcQuote);
        Assert.True(btcQuote.Ask > 1000);
        Assert.True(btcQuote.Bid > 1000);
        Assert.True(btcQuote.Bid <= btcQuote.Ask);
        Assert.True(btcQuote.Bid >= btcQuote.Ask * new decimal(0.80)); // Bid and ask should never be too far apart or something is wrong. 
        Assert.Equal("USD", btcQuote.FromAsset);
        Assert.Equal("BTC", btcQuote.ToAsset);



        // TODO Test: Kraken request signing / hash
        // TODO make these tests generic so we can test any custodian we want. Split out into ICustodian, ICanTrade, ICanDeposit and ICanWithdraw

        // TODO Test: WithdrawAsync();
        // TODO Test: TradeMarketAsync();
        // TODO Test: GetAssetBalancesAsync();
        // TODO Test: GetDepositAddressAsync();
        // TODO Test: GetTradeInfoAsync();
        // TODO Test: GetWithdrawalInfoAsync();

    }
}
