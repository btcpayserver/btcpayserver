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
using Newtonsoft.Json.Linq;
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
            public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
            {
                DateTimeOffset now = DateTimeOffset.UtcNow;
                try
                {
                    return await _inner.GetRatesAsync(cancellationToken);
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
            public TimeSpan Latency { get; set; }
            public PairRate[] PairRates { get; set; }
            public ExchangeException Exception { get; internal set; }
            public string Exchange { get; internal set; }
        }
        public RateProviderFactory(IHttpClientFactory httpClientFactory)
        {
            _httpClientFactory = httpClientFactory;
            InitExchanges();
        }
        private readonly IHttpClientFactory _httpClientFactory;
        private readonly Dictionary<string, IRateProvider> _DirectProviders = new Dictionary<string, IRateProvider>();
        public Dictionary<string, IRateProvider> Providers
        {
            get
            {
                return _DirectProviders;
            }
        }
        internal IEnumerable<AvailableRateProvider> GetDirectlySupportedExchanges()
        {
            yield return new AvailableRateProvider("binance", "Binance", "https://api.binance.com/api/v1/ticker/24hr");
            yield return new AvailableRateProvider("bittrex", "Bittrex", "https://bittrex.com/api/v1.1/public/getmarketsummaries");
            yield return new AvailableRateProvider("poloniex", "Poloniex", "https://poloniex.com/public?command=returnTicker");
            yield return new AvailableRateProvider("hitbtc", "HitBTC", "https://api.hitbtc.com/api/2/public/ticker");
            yield return new AvailableRateProvider("ndax", "NDAX", "https://ndax.io/api/returnTicker");

            yield return new AvailableRateProvider("coingecko", "CoinGecko", "https://api.coingecko.com/api/v3/exchange_rates");
            yield return new AvailableRateProvider("kraken", "Kraken", "https://api.kraken.com/0/public/Ticker?pair=ATOMETH,ATOMEUR,ATOMUSD,ATOMXBT,BATETH,BATEUR,BATUSD,BATXBT,BCHEUR,BCHUSD,BCHXBT,DAIEUR,DAIUSD,DAIUSDT,DASHEUR,DASHUSD,DASHXBT,EOSETH,EOSXBT,ETHCHF,ETHDAI,ETHUSDC,ETHUSDT,GNOETH,GNOXBT,ICXETH,ICXEUR,ICXUSD,ICXXBT,LINKETH,LINKEUR,LINKUSD,LINKXBT,LSKETH,LSKEUR,LSKUSD,LSKXBT,NANOETH,NANOEUR,NANOUSD,NANOXBT,OMGETH,OMGEUR,OMGUSD,OMGXBT,PAXGETH,PAXGEUR,PAXGUSD,PAXGXBT,SCETH,SCEUR,SCUSD,SCXBT,USDCEUR,USDCUSD,USDCUSDT,USDTCAD,USDTEUR,USDTGBP,USDTZUSD,WAVESETH,WAVESEUR,WAVESUSD,WAVESXBT,XBTCHF,XBTDAI,XBTUSDC,XBTUSDT,XDGEUR,XDGUSD,XETCXETH,XETCXXBT,XETCZEUR,XETCZUSD,XETHXXBT,XETHZCAD,XETHZEUR,XETHZGBP,XETHZJPY,XETHZUSD,XLTCXXBT,XLTCZEUR,XLTCZUSD,XMLNXETH,XMLNXXBT,XMLNZEUR,XMLNZUSD,XREPXETH,XREPXXBT,XREPZEUR,XXBTZCAD,XXBTZEUR,XXBTZGBP,XXBTZJPY,XXBTZUSD,XXDGXXBT,XXLMXXBT,XXMRXXBT,XXMRZEUR,XXMRZUSD,XXRPXXBT,XXRPZEUR,XXRPZUSD,XZECXXBT,XZECZEUR,XZECZUSD");
            yield return new AvailableRateProvider("bylls", "Bylls", "https://bylls.com/api/price?from_currency=BTC&to_currency=CAD");
            yield return new AvailableRateProvider("bitbank", "Bitbank", "https://public.bitbank.cc/prices");
            yield return new AvailableRateProvider("bitpay", "Bitpay", "https://bitpay.com/rates");
        }
        void InitExchanges()
        {
            // We need to be careful to only add exchanges which OnGetTickers implementation make only 1 request
            Providers.Add("binance", new ExchangeSharpRateProvider(new ExchangeBinanceAPI(), true));
            Providers.Add("bittrex", new ExchangeSharpRateProvider(new ExchangeBittrexAPI(), true));
            Providers.Add("poloniex", new ExchangeSharpRateProvider(new ExchangePoloniexAPI(), true));
            Providers.Add("hitbtc", new ExchangeSharpRateProvider(new ExchangeHitBTCAPI(), true));
            Providers.Add("ndax", new ExchangeSharpRateProvider(new ExchangeNDAXAPI(), true));

            // Handmade providers
            Providers.Add("coingecko", new CoinGeckoRateProvider(_httpClientFactory));
            Providers.Add("kraken", new KrakenExchangeRateProvider() { HttpClient = _httpClientFactory?.CreateClient("EXCHANGE_KRAKEN") });
            Providers.Add("bylls", new ByllsRateProvider(_httpClientFactory?.CreateClient("EXCHANGE_BYLLS")));
            Providers.Add("bitbank", new BitbankRateProvider(_httpClientFactory?.CreateClient("EXCHANGE_BITBANK")));
            Providers.Add("bitpay", new BitpayRateProvider(_httpClientFactory?.CreateClient("EXCHANGE_BITPAY")));


            // Backward compatibility: coinaverage should be using coingecko to prevent stores from breaking
            Providers.Add("coinaverage", new CoinGeckoRateProvider(_httpClientFactory));

            // Those exchanges make multiple requests when calling GetTickers so we remove them
            //DirectProviders.Add("gemini", new ExchangeSharpRateProvider("gemini", new ExchangeGeminiAPI()));
            //DirectProviders.Add("bitfinex", new ExchangeSharpRateProvider("bitfinex", new ExchangeBitfinexAPI()));
            //DirectProviders.Add("okex", new ExchangeSharpRateProvider("okex", new ExchangeOkexAPI()));
            //DirectProviders.Add("bitstamp", new ExchangeSharpRateProvider("bitstamp", new ExchangeBitstampAPI()));

            foreach (var provider in Providers.ToArray())
            {
                var prov = new BackgroundFetcherRateProvider(provider.Key, Providers[provider.Key]);
                prov.RefreshRate = TimeSpan.FromMinutes(1.0);
                prov.ValidatyTime = TimeSpan.FromMinutes(5.0);
                Providers[provider.Key] = prov;
            }

            foreach (var supportedExchange in GetCoinGeckoSupportedExchanges())
            {
                if (!Providers.ContainsKey(supportedExchange.Id) && supportedExchange.Id != CoinGeckoRateProvider.CoinGeckoName)
                {
                    var coingecko = new CoinGeckoRateProvider(_httpClientFactory)
                    {
                        UnderlyingExchange = supportedExchange.Id
                    };
                    var bgFetcher = new BackgroundFetcherRateProvider(supportedExchange.Id, coingecko);
                    bgFetcher.RefreshRate = TimeSpan.FromMinutes(1.0);
                    bgFetcher.ValidatyTime = TimeSpan.FromMinutes(5.0);
                    Providers.Add(supportedExchange.Id, bgFetcher);
                }
            }
        }

        IEnumerable<AvailableRateProvider> _AvailableRateProviders = null;
        public IEnumerable<AvailableRateProvider> GetSupportedExchanges()
        {
            if (_AvailableRateProviders == null)
            {
                var availableProviders = new Dictionary<string, AvailableRateProvider>();
                foreach (var exchange in GetDirectlySupportedExchanges())
                {
                    availableProviders.Add(exchange.Id, exchange);
                }
                foreach (var exchange in GetCoinGeckoSupportedExchanges())
                {
                    availableProviders.TryAdd(exchange.Id, exchange);
                }
                _AvailableRateProviders = availableProviders.Values.OrderBy(o => o.Name).ToArray();
            }
            return _AvailableRateProviders;
        }

        internal IEnumerable<AvailableRateProvider> GetCoinGeckoSupportedExchanges()
        {
            return JArray.Parse(CoinGeckoRateProvider.SupportedExchanges).Select(token =>
                    new AvailableRateProvider(Normalize(token["id"].ToString().ToLowerInvariant()), token["id"].ToString().ToLowerInvariant(), token["name"].ToString(),
                        $"https://api.coingecko.com/api/v3/exchanges/{token["id"]}/tickers", RateSource.Coingecko))
                .Concat(new[] { new AvailableRateProvider("gdax", "gdax", string.Empty, $"https://api.coingecko.com/api/v3/exchanges/gdax", RateSource.Coingecko) });
        }

        private string Normalize(string name)
        {
            if (name == "oasis_trade")
                return "oasisdex";
            if (name == "gdax")
                return "coinbasepro";
            return name;
        }

        public async Task<QueryRateResult> QueryRates(string exchangeName, CancellationToken cancellationToken)
        {
            Providers.TryGetValue(exchangeName, out var directProvider);
            directProvider = directProvider ?? NullRateProvider.Instance;

            var wrapper = new WrapperRateProvider(directProvider);
            var value = await wrapper.GetRatesAsync(cancellationToken);
            return new QueryRateResult()
            {
                Exchange = exchangeName,
                Latency = wrapper.Latency,
                PairRates = value,
                Exception = wrapper.Exception != null ? new ExchangeException() { Exception = wrapper.Exception, ExchangeName = exchangeName } : null
            };
        }
    }
}
