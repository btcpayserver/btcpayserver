using System;
using System.Collections.Concurrent;
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
    public class ExchangeException
    {
        public Exception Exception { get; set; }
        public string ExchangeName { get; set; }
    }
    public class RateResult
    {
        public List<ExchangeException> ExchangeExceptions { get; set; } = new List<ExchangeException>();
        public string Rule { get; set; }
        public string EvaluatedRule { get; set; }
        public HashSet<RateRulesErrors> Errors { get; set; }
        public BidAsk BidAsk { get; set; }
        public bool Cached { get; internal set; }
    }

    public class BTCPayRateProviderFactory
    {
        class QueryRateResult
        {
            public bool CachedResult { get; set; }
            public List<ExchangeException> Exceptions { get; set; }
            public ExchangeRates ExchangeRates { get; set; }
        }
        IMemoryCache _Cache;
        private IOptions<MemoryCacheOptions> _CacheOptions;
        private readonly IHttpClientFactory _httpClientFactory;

        public IMemoryCache Cache
        {
            get
            {
                return _Cache;
            }
        }
        CoinAverageSettings _CoinAverageSettings;
        public BTCPayRateProviderFactory(IOptions<MemoryCacheOptions> cacheOptions,
                                         IHttpClientFactory httpClientFactory,
                                         BTCPayNetworkProvider btcpayNetworkProvider,
                                         CoinAverageSettings coinAverageSettings)
        {
            if (cacheOptions == null)
                throw new ArgumentNullException(nameof(cacheOptions));
            _CoinAverageSettings = coinAverageSettings;
            _Cache = new MemoryCache(cacheOptions);
            _CacheOptions = cacheOptions;
            _httpClientFactory = httpClientFactory;
            // We use 15 min because of limits with free version of bitcoinaverage
            CacheSpan = TimeSpan.FromMinutes(15.0);
            this.btcpayNetworkProvider = btcpayNetworkProvider;
            InitExchanges();
        }

        public bool UseCoinAverageAsFallback { get; set; } = true;

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
            DirectProviders.Add("kraken", new KrakenExchangeRateProvider() {  HttpClient = _httpClientFactory?.CreateClient() });
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

        private readonly Dictionary<string, IRateProvider> _DirectProviders = new Dictionary<string, IRateProvider>();
        public Dictionary<string, IRateProvider> DirectProviders
        {
            get
            {
                return _DirectProviders;
            }
        }


        BTCPayNetworkProvider btcpayNetworkProvider;
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

        public async Task<RateResult> FetchRate(CurrencyPair pair, RateRules rules)
        {
            return await FetchRates(new HashSet<CurrencyPair>(new[] { pair }), rules).First().Value;
        }

        public Dictionary<CurrencyPair, Task<RateResult>> FetchRates(HashSet<CurrencyPair> pairs, RateRules rules)
        {
            if (rules == null)
                throw new ArgumentNullException(nameof(rules));

            var fetchingRates = new Dictionary<CurrencyPair, Task<RateResult>>();
            var fetchingExchanges = new Dictionary<string, Task<QueryRateResult>>();
            var consolidatedRates = new ExchangeRates();

            foreach (var i in pairs.Select(p => (Pair: p, RateRule: rules.GetRuleFor(p))))
            {
                var dependentQueries = new List<Task<QueryRateResult>>();
                foreach (var requiredExchange in i.RateRule.ExchangeRates)
                {
                    if (!fetchingExchanges.TryGetValue(requiredExchange.Exchange, out var fetching))
                    {
                        fetching = QueryRates(requiredExchange.Exchange);
                        fetchingExchanges.Add(requiredExchange.Exchange, fetching);
                    }
                    dependentQueries.Add(fetching);
                }
                fetchingRates.Add(i.Pair, GetRuleValue(dependentQueries, i.RateRule));
            }
            return fetchingRates;
        }

        private async Task<RateResult> GetRuleValue(List<Task<QueryRateResult>> dependentQueries, RateRule rateRule)
        {
            var result = new RateResult();
            result.Cached = true;
            foreach (var queryAsync in dependentQueries)
            {
                var query = await queryAsync;
                if (!query.CachedResult)
                    result.Cached = false;
                result.ExchangeExceptions.AddRange(query.Exceptions);
                foreach (var rule in query.ExchangeRates)
                {
                    rateRule.ExchangeRates.SetRate(rule.Exchange, rule.CurrencyPair, rule.BidAsk);
                }
            }
            rateRule.Reevaluate();
            result.BidAsk = rateRule.BidAsk;
            result.Errors = rateRule.Errors;
            result.EvaluatedRule = rateRule.ToString(true);
            result.Rule = rateRule.ToString(false);
            return result;
        }


        private async Task<QueryRateResult> QueryRates(string exchangeName)
        {
            List<IRateProvider> providers = new List<IRateProvider>();
            if (DirectProviders.TryGetValue(exchangeName, out var directProvider))
                providers.Add(directProvider);
            if (_CoinAverageSettings.AvailableExchanges.ContainsKey(exchangeName))
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
