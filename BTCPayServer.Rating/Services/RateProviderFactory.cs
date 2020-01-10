using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using ExchangeSharp;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using MemoryCache = Microsoft.Extensions.Caching.Memory.MemoryCache;

namespace BTCPayServer.Services.Rates
{
    public class RateProviderFactory
    {
        class WrapperRateProvider : IRateProvider
        {
            private readonly IRateProvider _inner;
            public Exception Exception { get; private set; }
            public TimeSpan Latency { get; set; }
            public WrapperRateProvider(IRateProvider inner)
            {
                _inner = inner;
            }
            public async Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                try
                {
                    return await _inner.GetRatesAsync(cancellationToken);
                }
                catch (Exception ex)
                {
                    Exception = ex;
                    return new ExchangeRates();
                }
                finally
                {
                    Latency = DateTimeOffset.UtcNow - now;
                }
            }
        }
        public class QueryRateResult
        {
            public TimeSpan Latency { get; set; }
            public ExchangeRates ExchangeRates { get; set; }
            public ExchangeException Exception { get; internal set; }
        }
        public RateProviderFactory(IOptions<MemoryCacheOptions> cacheOptions,
                                   IHttpClientFactory httpClientFactory,
                                   CoinAverageSettings coinAverageSettings)
        {
            _httpClientFactory = httpClientFactory;
            _CoinAverageSettings = coinAverageSettings;
            _CacheOptions = cacheOptions;
            // We use 15 min because of limits with free version of bitcoinaverage
            CacheSpan = TimeSpan.FromMinutes(15.0);
        }
        private IOptions<MemoryCacheOptions> _CacheOptions;
        TimeSpan _CacheSpan;
        public TimeSpan CacheSpan
        {
            get
            {
                return _CacheSpan;
            }
            set
            {
                _CacheSpan = value;
                InvalidateCache();
            }
        }
        public void InvalidateCache()
        {
            var cache = new MemoryCache(_CacheOptions);
            foreach (var provider in Providers.Select(p => p.Value as CachedRateProvider).Where(p => p != null))
            {
                provider.CacheSpan = CacheSpan;
                provider.MemoryCache = cache;
            }
            if (Providers.TryGetValue(CoinGeckoRateProvider.CoinGeckoName, out var coinAverage) && coinAverage is BackgroundFetcherRateProvider c)
            {
                c.RefreshRate = CacheSpan;
                c.ValidatyTime = CacheSpan + TimeSpan.FromMinutes(1.0);
            }
        }
        CoinAverageSettings _CoinAverageSettings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Dictionary<string, IRateProvider> _DirectProviders = new Dictionary<string, IRateProvider>();
        public Dictionary<string, IRateProvider> Providers
        {
            get
            {
                return _DirectProviders;
            }
        }

        public async Task InitExchanges()
        {
            // We need to be careful to only add exchanges which OnGetTickers implementation make only 1 request
            Providers.Add("binance", new ExchangeSharpRateProvider("binance", new ExchangeBinanceAPI(), true));
            Providers.Add("bittrex", new ExchangeSharpRateProvider("bittrex", new ExchangeBittrexAPI(), true));
            Providers.Add("poloniex", new ExchangeSharpRateProvider("poloniex", new ExchangePoloniexAPI(), true));
            Providers.Add("hitbtc", new ExchangeSharpRateProvider("hitbtc", new ExchangeHitBTCAPI(), true));
            Providers.Add("ndax", new ExchangeSharpRateProvider("ndax", new ExchangeNDAXAPI(), true));

            // Cryptopia is often not available
            // Disabled because of https://twitter.com/Cryptopia_NZ/status/1085084168852291586
            // Providers.Add("cryptopia", new ExchangeSharpRateProvider("cryptopia", new ExchangeCryptopiaAPI(), false));

            // Handmade providers
            Providers.Add(CoinGeckoRateProvider.CoinGeckoName, new CoinGeckoRateProvider(_httpClientFactory));
            Providers.Add(CoinAverageRateProvider.CoinAverageName, new CoinAverageRateProvider() { Exchange = CoinAverageRateProvider.CoinAverageName, HttpClient = _httpClientFactory?.CreateClient("EXCHANGE_COINAVERAGE"), Authenticator = _CoinAverageSettings });
            Providers.Add("kraken", new KrakenExchangeRateProvider() { HttpClient = _httpClientFactory?.CreateClient("EXCHANGE_KRAKEN") });
            Providers.Add("bylls", new ByllsRateProvider(_httpClientFactory?.CreateClient("EXCHANGE_BYLLS")));
            Providers.Add("bitbank", new BitbankRateProvider(_httpClientFactory?.CreateClient("EXCHANGE_BITBANK")));
            Providers.Add("bitpay", new BitpayRateProvider(_httpClientFactory?.CreateClient("EXCHANGE_BITPAY")));

            // Those exchanges make multiple requests when calling GetTickers so we remove them
            //DirectProviders.Add("gemini", new ExchangeSharpRateProvider("gemini", new ExchangeGeminiAPI()));
            //DirectProviders.Add("bitfinex", new ExchangeSharpRateProvider("bitfinex", new ExchangeBitfinexAPI()));
            //DirectProviders.Add("okex", new ExchangeSharpRateProvider("okex", new ExchangeOkexAPI()));
            //DirectProviders.Add("bitstamp", new ExchangeSharpRateProvider("bitstamp", new ExchangeBitstampAPI()));

            foreach (var provider in Providers.ToArray())
            {
                if (provider.Key == "cryptopia") // Shitty exchange, rate often unavailable, it spams the logs
                    continue;
                var prov = new BackgroundFetcherRateProvider(provider.Key, Providers[provider.Key]);
                if(provider.Key == CoinGeckoRateProvider.CoinGeckoName)
                {
                    prov.RefreshRate = CacheSpan;
                    prov.ValidatyTime = CacheSpan + TimeSpan.FromMinutes(1.0);
                }
                else
                {
                    prov.RefreshRate = TimeSpan.FromMinutes(1.0);
                    prov.ValidatyTime = TimeSpan.FromMinutes(5.0);
                }
                Providers[provider.Key] = prov;
            }

            var cache = new MemoryCache(_CacheOptions);
            foreach (var supportedExchange in await GetSupportedExchanges(true))
            {
                if (!Providers.ContainsKey(supportedExchange.Id))
                {
                    var coinAverage = new CoinGeckoRateProvider(_httpClientFactory)
                    {
                        Exchange = supportedExchange.Id
                    };
                    var cached = new CachedRateProvider(supportedExchange.Id, coinAverage, cache)
                    {
                        CacheSpan = CacheSpan
                    };
                    Providers.Add(supportedExchange.Id, cached);
                }
            }
        }

        public async Task<IEnumerable<AvailableRateProvider>> GetSupportedExchanges(bool reload = false)
        {
            IEnumerable<AvailableRateProvider> exchanges;
            switch (Providers[CoinGeckoRateProvider.CoinGeckoName])
            {
                case BackgroundFetcherRateProvider backgroundFetcherRateProvider:
                    exchanges = await ((CoinGeckoRateProvider)((BackgroundFetcherRateProvider)Providers[
                        CoinGeckoRateProvider.CoinGeckoName]).Inner).GetAvailableExchanges(reload);
                    break;
                case CoinGeckoRateProvider coinGeckoRateProvider:
                    exchanges = await coinGeckoRateProvider.GetAvailableExchanges(reload);
                    break;
                default:
                    exchanges = new AvailableRateProvider[0];
                    break;
            }
            // Add other exchanges supported here
            return new[]
            {
                new AvailableRateProvider(CoinGeckoRateProvider.CoinGeckoName, "Coin Gecko",
                    "https://api.coingecko.com/api/v3/exchange_rates"),
                new AvailableRateProvider("bylls", "Bylls",
                    "https://bylls.com/api/price?from_currency=BTC&to_currency=CAD"),
                new AvailableRateProvider("ndax", "NDAX", "https://ndax.io/api/returnTicker"),
                new AvailableRateProvider("bitbank", "Bitbank", "https://public.bitbank.cc/prices"),
                new AvailableRateProvider(CoinAverageRateProvider.CoinAverageName, "Coin Average",
                    "https://apiv2.bitcoinaverage.com/indices/global/ticker/short")
            }.Concat(exchanges);
        }

        public async Task<QueryRateResult> QueryRates(string exchangeName, CancellationToken cancellationToken)
        {
            Providers.TryGetValue(exchangeName, out var directProvider);
            directProvider = directProvider ?? NullRateProvider.Instance;

            var wrapper = new WrapperRateProvider(directProvider);
            var value = await wrapper.GetRatesAsync(cancellationToken);
            return new QueryRateResult()
            {
                Latency = wrapper.Latency,
                ExchangeRates = value,
                Exception = wrapper.Exception != null ? new ExchangeException() { Exception = wrapper.Exception, ExchangeName = exchangeName } : null
            };
        }
    }
}
