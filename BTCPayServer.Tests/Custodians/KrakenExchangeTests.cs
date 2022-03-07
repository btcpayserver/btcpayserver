using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Custodians.Client.Exception;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Custodian.Client.Kraken;
using Microsoft.Extensions.Caching.Memory;
using Newtonsoft.Json.Linq;
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
        //

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
            KrakenAssetPair krakenAssetPair = (KrakenAssetPair)pair;
            Assert.NotNull(krakenAssetPair.PairCode);
        }

        Assert.NotNull(pairs[0].AssetBought);

        // Test: GetDepositablePaymentMethods();
        var depositablePaymentMethods = kraken.GetDepositablePaymentMethods();
        Assert.NotNull(depositablePaymentMethods);
        Assert.True(depositablePaymentMethods.Length > 0);
        Assert.Contains(KrakenMockHttpMessageHandler.GoodPaymentMethod, depositablePaymentMethods);


        // Test: GetWithdrawablePaymentMethods();
        var withdrawablePaymentMethods = kraken.GetWithdrawablePaymentMethods();
        Assert.NotNull(withdrawablePaymentMethods);
        Assert.True(withdrawablePaymentMethods.Length > 0);
        Assert.Contains(KrakenMockHttpMessageHandler.GoodPaymentMethod, withdrawablePaymentMethods);


        // Test: GetQuoteForAssetAsync();
        var btcQuote = await kraken.GetQuoteForAssetAsync(KrakenMockHttpMessageHandler.GoodFiat, KrakenMockHttpMessageHandler.GoodAsset, null, cancellationToken);
        Assert.NotNull(btcQuote);
        Assert.True(btcQuote.Ask > 1000);
        Assert.True(btcQuote.Bid > 1000);
        Assert.True(btcQuote.Bid <= btcQuote.Ask);
        Assert.True(btcQuote.Bid >= btcQuote.Ask * new decimal(0.80)); // Bid and ask should never be too far apart or something is wrong. 
        Assert.Equal(KrakenMockHttpMessageHandler.GoodFiat, btcQuote.FromAsset);
        Assert.Equal(KrakenMockHttpMessageHandler.GoodAsset, btcQuote.ToAsset);

// TODO this config is copy pasted. Put it somewhere we can reuse it.
        JObject goodConfig = JObject.Parse(@"{
'WithdrawToAddressNamePerPaymentMethod': {
   'BTC-OnChain': 'My Ledger Nano'
},
'ApiKey': 'APIKEY',
'PrivateKey': 'UFJJVkFURUtFWQ=='
}");

        JObject badConfig = JObject.Parse(@"{
'WithdrawToAddressNamePerPaymentMethod': {
   'BTC-OnChain': 'My Ledger Nano'
},
'ApiKey': 'APIKEY',
'PrivateKey': 'NOT-BASE-64'
}");


        var mockHttpMessageHandler = new KrakenMockHttpMessageHandler();
        var mockHttpClient = new HttpClient(mockHttpMessageHandler);
        var memoryCache = tester.PayTester.GetService<IMemoryCache>();
        var mockedKraken = new KrakenExchange(mockHttpClient, memoryCache);

        // Test: GetAssetBalancesAsync();
        var assetBalances = await mockedKraken.GetAssetBalancesAsync(goodConfig, cancellationToken);
        Assert.NotNull(assetBalances);
        Assert.True(assetBalances.Count == 8);
        Assert.Contains(assetBalances.Keys, item => item.Equals(KrakenMockHttpMessageHandler.GoodAsset));
        Assert.Contains(assetBalances.Keys, item => item.Equals(KrakenMockHttpMessageHandler.GoodFiat));

        // Test: GetAssetBalancesAsync() with bad config;
        await Assert.ThrowsAsync<BadConfigException>(async () => await mockedKraken.GetAssetBalancesAsync(badConfig, cancellationToken));


        // TODO Test: Kraken request signing / hash
        // TODO make these tests generic so we can test any custodian we want. Split out into ICustodian, ICanTrade, ICanDeposit and ICanWithdraw

        // TODO Test: WithdrawAsync(), wrong asset we don't have

        // TODO Test: WithdrawAsync(), correct asset, but unsupported payment method

        // TODO Test: WithdrawAsync(), asset in qty we don't have

        // TODO Test: WithdrawAsync(), correct use


        // TODO Test: TradeMarketAsync(), wrong assets

        // TODO Test: TradeMarketAsync(), qty we don't have, insufficient funds

        // TODO Test: TradeMarketAsync(), correct use


        // TODO Test: GetDepositAddressAsync(), wrong payment method

        // TODO Test: GetDepositAddressAsync(), correct use


        // TODO Test: GetTradeInfoAsync(), non-existing trade ID

        // TODO Test: GetTradeInfoAsync(), correct use


        // Test: GetWithdrawalInfoAsync(), bad config
        await Assert.ThrowsAsync<BadConfigException>(async () => await mockedKraken.GetWithdrawalInfoAsync(KrakenMockHttpMessageHandler.GoodPaymentMethod, KrakenMockHttpMessageHandler.NewWithdrawalId, badConfig, cancellationToken));

        // Test: GetWithdrawalInfoAsync(), non-existing withdrawal ID
        await Assert.ThrowsAsync<WithdrawalNotFoundException>(async () => await mockedKraken.GetWithdrawalInfoAsync(KrakenMockHttpMessageHandler.GoodPaymentMethod, KrakenMockHttpMessageHandler.BadWithdrawalId, goodConfig, cancellationToken));

        // Test: GetWithdrawalInfoAsync(), correct use
        var withdrawalInfo = await mockedKraken.GetWithdrawalInfoAsync(KrakenMockHttpMessageHandler.GoodPaymentMethod, KrakenMockHttpMessageHandler.NewWithdrawalId, goodConfig, cancellationToken);
        Assert.NotNull(withdrawalInfo);
        Assert.Equal(KrakenMockHttpMessageHandler.GoodAsset, withdrawalInfo.Asset);
        Assert.Equal(KrakenMockHttpMessageHandler.GoodPaymentMethod, withdrawalInfo.PaymentMethod);
        Assert.Equal(WithdrawalResponseData.WithdrawalStatus.Queued, withdrawalInfo.Status);

        Assert.Equal(2, withdrawalInfo.LedgerEntries.Count);

        Assert.Equal(KrakenMockHttpMessageHandler.GoodAsset, withdrawalInfo.LedgerEntries[0].Asset);
        Assert.Equal(-1 * KrakenMockHttpMessageHandler.WithdrawalAmountExclFee , withdrawalInfo.LedgerEntries[0].Qty);
        Assert.Equal(LedgerEntryData.LedgerEntryType.Withdrawal, withdrawalInfo.LedgerEntries[0].Type);

        Assert.Equal(KrakenMockHttpMessageHandler.GoodAsset, withdrawalInfo.LedgerEntries[1].Asset);
        Assert.Equal(-1 * KrakenMockHttpMessageHandler.ExpectedWithdrawalFee, withdrawalInfo.LedgerEntries[1].Qty);
        Assert.Equal(LedgerEntryData.LedgerEntryType.Fee, withdrawalInfo.LedgerEntries[1].Type);

        Assert.Equal(KrakenMockHttpMessageHandler.TargetWithdrawalAddress, withdrawalInfo.TargetAddress);
        Assert.Null(withdrawalInfo.TransactionId);
        Assert.Equal(KrakenMockHttpMessageHandler.NewWithdrawalId, withdrawalInfo.WithdrawalId);
        Assert.NotNull(withdrawalInfo.CreatedTime);
    }
}
