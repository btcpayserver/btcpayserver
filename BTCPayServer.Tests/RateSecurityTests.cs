using System;
using System.IO;
using System.Net;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using Xunit;

namespace BTCPayServer.Tests;

[Trait("Fast", "Fast")]
public class RateSecurityTests
{
    [Fact]
    public async Task BackgroundFetcherDropsInvalidProviderRates()
    {
        var pair = CurrencyPair.Parse("BTC_USD");
        var invalidPair = new CurrencyPair("BTC", "USD|999|999");
        var provider = new FixedRateProvider(
            new PairRate(pair, new BidAsk(1m, 2m)),
            new PairRate(pair, new BidAsk(-2m, -1m)),
            new PairRate(pair, new BidAsk(0m)),
            new PairRate(invalidPair, new BidAsk(1m)));
        var fetcher = new BackgroundFetcherRateProvider(provider);

        var rate = Assert.Single(await fetcher.GetRatesAsync(CancellationToken.None));
        Assert.Equal(pair, rate.CurrencyPair);
        Assert.Equal(new BidAsk(1m, 2m), rate.BidAsk);
        Assert.Single(fetcher.GetState().Rates);

        var loaded = new BackgroundFetcherRateProvider(new FixedRateProvider());
        loaded.LoadState(new BackgroundFetcherState
        {
            LastUpdated = DateTimeOffset.UtcNow,
            Rates =
            [
                new BackgroundFetcherRate { Pair = pair, BidAsk = new BidAsk(1m, 2m) },
                new BackgroundFetcherRate { Pair = pair, BidAsk = new BidAsk(-2m, -1m) },
                new BackgroundFetcherRate { Pair = invalidPair, BidAsk = new BidAsk(1m) }
            ]
        });
        Assert.Single(await loaded.GetRatesAsync(CancellationToken.None));
    }

    [Fact]
    public async Task RateProvidersRejectOversizedResponses()
    {
        var body = $"{{\"padding\":\"{new string('x', 1024 * 1024)}\"}}";
        using var client = new HttpClient(new TestHttpMessageHandler(_ =>
            new HttpResponseMessage(HttpStatusCode.OK)
            {
                Content = new UnknownLengthContent(body)
            }));
        IRateProvider[] providers =
        [
            new BitcoinKenyaRateProvider(client),
            new BitbankRateProvider(client),
            new RipioExchangeProvider(client)
        ];

        foreach (var provider in providers)
            await Assert.ThrowsAsync<HttpRequestException>(() => provider.GetRatesAsync(CancellationToken.None));
    }

    private sealed class FixedRateProvider(params PairRate[] rates) : IRateProvider
    {
        public RateSourceInfo RateSourceInfo => new("fixed", "Fixed", "https://example.com");

        public Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
            => Task.FromResult(rates);
    }

    private sealed class UnknownLengthContent : HttpContent
    {
        private readonly byte[] _body;

        public UnknownLengthContent(string body)
        {
            _body = Encoding.UTF8.GetBytes(body);
            Headers.ContentType = new MediaTypeHeaderValue("application/json");
        }

        protected override Task SerializeToStreamAsync(Stream stream, TransportContext context)
            => stream.WriteAsync(_body, 0, _body.Length);

        protected override bool TryComputeLength(out long length)
        {
            length = 0;
            return false;
        }
    }
}
