using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using ExchangeSharp;
using Microsoft.Extensions.Caching.Memory;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Services.Rates
{
    public class RateProviderFactory
    {
        public class QueryRateResult
        {
            public bool CachedResult { get; set; }
            public List<ExchangeException> Exceptions { get; set; }
            public ExchangeRates ExchangeRates { get; set; }
        }
        public RateProviderFactory(IOptions<MemoryCacheOptions> cacheOptions,
                                   IHttpClientFactory httpClientFactory,
                                   CoinAverageSettings coinAverageSettings)
        {
            _httpClientFactory = httpClientFactory;
            _CoinAverageSettings = coinAverageSettings;
            _Cache = new MemoryCache(cacheOptions);
            _CacheOptions = cacheOptions;
            // We use 15 min because of limits with free version of bitcoinaverage
            CacheSpan = TimeSpan.FromMinutes(15.0);
            InitExchanges();
        }
        IMemoryCache _Cache;
        private IOptions<MemoryCacheOptions> _CacheOptions;

        public IMemoryCache Cache
        {
            get
            {
                return _Cache;
            }
        }
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
            _Cache = new MemoryCache(_CacheOptions);
        }
        CoinAverageSettings _CoinAverageSettings;
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Dictionary<string, IRateProvider> _DirectProviders = new Dictionary<string, IRateProvider>();
        public Dictionary<string, IRateProvider> DirectProviders
        {
            get
            {
                return _DirectProviders;
            }
        }

        private void InitExchanges()
        {
            // We need to be careful to only add exchanges which OnGetTickers implementation make only 1 request
            DirectProviders.Add("binance", new ExchangeSharpRateProvider("binance", new ExchangeBinanceAPI(), true));
            DirectProviders.Add("bittrex", new ExchangeSharpRateProvider("bittrex", new ExchangeBittrexAPI(), true));
            DirectProviders.Add("poloniex", new ExchangeSharpRateProvider("poloniex", new ExchangePoloniexAPI(), true));
            DirectProviders.Add("hitbtc", new ExchangeSharpRateProvider("hitbtc", new ExchangeHitbtcAPI(), false));
            DirectProviders.Add("cryptopia", new ExchangeSharpRateProvider("cryptopia", new ExchangeCryptopiaAPI(), false));

            // Handmade providers
            DirectProviders.Add("bitpay", new BitpayRateProvider(new NBitpayClient.Bitpay(new NBitcoin.Key(), new Uri("https://bitpay.com/"))));
            DirectProviders.Add(QuadrigacxRateProvider.QuadrigacxName, new QuadrigacxRateProvider());
            DirectProviders.Add(CoinAverageRateProvider.CoinAverageName, new CoinAverageRateProvider() { Exchange = CoinAverageRateProvider.CoinAverageName, HttpClient = _httpClientFactory?.CreateClient(), Authenticator = _CoinAverageSettings });

            // Those exchanges make multiple requests when calling GetTickers so we remove them
            DirectProviders.Add("kraken", new KrakenExchangeRateProvider() { HttpClient = _httpClientFactory?.CreateClient() });
            //DirectProviders.Add("gdax", new ExchangeSharpRateProvider("gdax", new ExchangeGdaxAPI()));
            //DirectProviders.Add("gemini", new ExchangeSharpRateProvider("gemini", new ExchangeGeminiAPI()));
            //DirectProviders.Add("bitfinex", new ExchangeSharpRateProvider("bitfinex", new ExchangeBitfinexAPI()));
            //DirectProviders.Add("okex", new ExchangeSharpRateProvider("okex", new ExchangeOkexAPI()));
            //DirectProviders.Add("bitstamp", new ExchangeSharpRateProvider("bitstamp", new ExchangeBitstampAPI()));
        }

        public CoinAverageExchanges GetSupportedExchanges()
        {
            CoinAverageExchanges exchanges = new CoinAverageExchanges();
            foreach (var exchange in _CoinAverageSettings.AvailableExchanges)
            {
                exchanges.Add(exchange.Value);
            }

            // Add other exchanges supported here
            exchanges.Add(new CoinAverageExchange(CoinAverageRateProvider.CoinAverageName, "Coin Average"));
            exchanges.Add(new CoinAverageExchange("cryptopia", "Cryptopia"));

            return exchanges;
        }

        public bool UseCoinAverageAsFallback { get; set; } = true;
        public async Task<QueryRateResult> QueryRates(string exchangeName)
        {
            List<IRateProvider> providers = new List<IRateProvider>();
            if (DirectProviders.TryGetValue(exchangeName, out var directProvider))
                providers.Add(directProvider);
            if (UseCoinAverageAsFallback && _CoinAverageSettings.AvailableExchanges.ContainsKey(exchangeName))
            {
                providers.Add(new CoinAverageRateProvider()
                {
                    Exchange = exchangeName,
                    HttpClient = _httpClientFactory?.CreateClient(),
                    Authenticator = _CoinAverageSettings
                });
            }
            var fallback = new FallbackRateProvider(providers.ToArray());
            var cached = new CachedRateProvider(exchangeName, fallback, _Cache)
            {
                CacheSpan = CacheSpan
            };
            var value = await cached.GetRatesAsync();
            return new QueryRateResult()
            {
                CachedResult = !fallback.Used,
                ExchangeRates = value,
                Exceptions = fallback.Exceptions
                .Select(c => new ExchangeException() { Exception = c, ExchangeName = exchangeName }).ToList()
            };
        }
    }
}
