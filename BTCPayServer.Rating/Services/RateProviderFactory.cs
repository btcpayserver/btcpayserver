#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;

namespace BTCPayServer.Services.Rates
{
    public class RateProviderFactory
    {
        class WrapperRateProvider
        {
            private readonly IRateProvider _inner;
            public Exception? Exception { get; private set; }
            public TimeSpan Latency { get; set; }
            public WrapperRateProvider(IRateProvider inner)
            {
                _inner = inner;
            }
            public async Task<PairRate[]> GetRatesAsync(IRateContext? context, CancellationToken cancellationToken)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                try
                {
					return await _inner.GetRatesAsyncWithMaybeContext(context, cancellationToken);
                }
                catch (Exception ex)
                {
                    Exception = ex;
                    return Array.Empty<PairRate>();
                }
                finally
                {
                    Latency = DateTimeOffset.UtcNow - now;
                }
            }
        }
        public class QueryRateResult
        {
            public QueryRateResult(string exchangeName, TimeSpan latency, PairRate[] pairRates)
            {
                Exchange = exchangeName;
                Latency = latency;
                PairRates = pairRates;
            }

            public TimeSpan Latency { get; set; }
            public PairRate[] PairRates { get; set; }
            public ExchangeException? Exception { get; internal set; }
            public string Exchange { get; internal set; }
        }
        public RateProviderFactory(IHttpClientFactory httpClientFactory,IEnumerable<IRateProvider> rateProviders)
        {
            _httpClientFactory = httpClientFactory;
            foreach (var prov in rateProviders)
            {
                Providers.Add(prov.RateSourceInfo.Id, prov);
            }
            InitExchanges();
        }
        private readonly IHttpClientFactory _httpClientFactory;
        public Dictionary<string, IRateProvider> Providers { get; } = new Dictionary<string, IRateProvider>();
        void InitExchanges()
        {
            foreach (var provider in Providers.ToArray())
            {
                var prov = Providers[provider.Key];
                if (prov is IContextualRateProvider)
                {
                    Providers[provider.Key] = prov;
                }
                else
                {
                    var prov2 = new BackgroundFetcherRateProvider(prov);
                    prov2.RefreshRate = TimeSpan.FromMinutes(1.0);
                    prov2.ValidatyTime = TimeSpan.FromMinutes(5.0);
                    Providers[provider.Key] = prov2;
                }
                var rsi = provider.Value.RateSourceInfo;
                AvailableRateProviders.Add(new(rsi.Id, rsi.DisplayName, rsi.Url));
            }

            foreach (var supportedExchange in CoinGeckoRateProvider.SupportedExchanges.Values)
            {
                if (!Providers.ContainsKey(supportedExchange.Id) && supportedExchange.Id != CoinGeckoRateProvider.CoinGeckoName)
                {
                    var coingecko = new CoinGeckoRateProvider(_httpClientFactory)
                    {
                        UnderlyingExchange = supportedExchange.Id
                    };
                    var bgFetcher = new BackgroundFetcherRateProvider(coingecko);
                    bgFetcher.RefreshRate = TimeSpan.FromMinutes(1.0);
                    bgFetcher.ValidatyTime = TimeSpan.FromMinutes(5.0);
                    Providers.Add(supportedExchange.Id, bgFetcher);
                    AvailableRateProviders.Add(coingecko.RateSourceInfo);
                }
            }
            AvailableRateProviders.Sort((a, b) => StringComparer.Ordinal.Compare(a.DisplayName, b.DisplayName));
        }

        public List<RateSourceInfo> AvailableRateProviders { get; } = new List<RateSourceInfo>();

        public async Task<QueryRateResult> QueryRates(string exchangeName, IRateContext? context = null, CancellationToken cancellationToken = default)
        {
            Providers.TryGetValue(exchangeName, out var directProvider);
            directProvider ??= NullRateProvider.Instance;

            var wrapper = new WrapperRateProvider(directProvider);
            var value = await wrapper.GetRatesAsync(context, cancellationToken);
            return new QueryRateResult(exchangeName, wrapper.Latency, value)
            {
                Exception = wrapper.Exception != null ? new ExchangeException() { Exception = wrapper.Exception, ExchangeName = exchangeName } : null
            };
        }
    }
}
