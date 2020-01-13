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
            InitExchanges();
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
        internal IEnumerable<AvailableRateProvider> GetDirectlySupportedExchanges()
        {
            yield return new AvailableRateProvider("binance", "Binance", "https://api.binance.com/api/v1/ticker/24hr", RateSource.Direct);
            yield return new AvailableRateProvider("bittrex", "Bittrex", "https://bittrex.com/api/v1.1/public/getmarketsummaries", RateSource.Direct);
            yield return new AvailableRateProvider("poloniex", "Poloniex", "https://poloniex.com/public?command=returnTicker", RateSource.Direct);
            yield return new AvailableRateProvider("hitbtc", "HitBTC", "https://api.hitbtc.com/api/2/public/ticker", RateSource.Direct);
            yield return new AvailableRateProvider("ndax", "NDAX", "https://ndax.io/api/returnTicker", RateSource.Direct);

            yield return new AvailableRateProvider(CoinGeckoRateProvider.CoinGeckoName, "Coin Gecko", "https://api.coingecko.com/api/v3/exchange_rates", RateSource.Direct);
            yield return new AvailableRateProvider(CoinAverageRateProvider.CoinAverageName, "Coin Average", "https://apiv2.bitcoinaverage.com/indices/global/ticker/short", RateSource.Direct);
            yield return new AvailableRateProvider("kraken", "Kraken", "https://api.kraken.com/0/public/Ticker?pair=ATOMETH,ATOMEUR,ATOMUSD,ATOMXBT,BATETH,BATEUR,BATUSD,BATXBT,BCHEUR,BCHUSD,BCHXBT,DAIEUR,DAIUSD,DAIUSDT,DASHEUR,DASHUSD,DASHXBT,EOSETH,EOSXBT,ETHCHF,ETHDAI,ETHUSDC,ETHUSDT,GNOETH,GNOXBT,ICXETH,ICXEUR,ICXUSD,ICXXBT,LINKETH,LINKEUR,LINKUSD,LINKXBT,LSKETH,LSKEUR,LSKUSD,LSKXBT,NANOETH,NANOEUR,NANOUSD,NANOXBT,OMGETH,OMGEUR,OMGUSD,OMGXBT,PAXGETH,PAXGEUR,PAXGUSD,PAXGXBT,SCETH,SCEUR,SCUSD,SCXBT,USDCEUR,USDCUSD,USDCUSDT,USDTCAD,USDTEUR,USDTGBP,USDTZUSD,WAVESETH,WAVESEUR,WAVESUSD,WAVESXBT,XBTCHF,XBTDAI,XBTUSDC,XBTUSDT,XDGEUR,XDGUSD,XETCXETH,XETCXXBT,XETCZEUR,XETCZUSD,XETHXXBT,XETHZCAD,XETHZEUR,XETHZGBP,XETHZJPY,XETHZUSD,XLTCXXBT,XLTCZEUR,XLTCZUSD,XMLNXETH,XMLNXXBT,XMLNZEUR,XMLNZUSD,XREPXETH,XREPXXBT,XREPZEUR,XXBTZCAD,XXBTZEUR,XXBTZGBP,XXBTZJPY,XXBTZUSD,XXDGXXBT,XXLMXXBT,XXMRXXBT,XXMRZEUR,XXMRZUSD,XXRPXXBT,XXRPZEUR,XXRPZUSD,XZECXXBT,XZECZEUR,XZECZUSD", RateSource.Direct);
            yield return new AvailableRateProvider("bylls", "Bylls", "https://bylls.com/api/price?from_currency=BTC&to_currency=CAD", RateSource.Direct);
            yield return new AvailableRateProvider("bitbank", "Bitbank", "https://public.bitbank.cc/prices", RateSource.Direct);
            yield return new AvailableRateProvider("bitpay", "Bitpay", "https://bitpay.com/rates", RateSource.Direct);
        }
        void InitExchanges()
        {
            // We need to be careful to only add exchanges which OnGetTickers implementation make only 1 request
            Providers.Add("binance", new ExchangeSharpRateProvider("binance", new ExchangeBinanceAPI(), true));
            Providers.Add("bittrex", new ExchangeSharpRateProvider("bittrex", new ExchangeBittrexAPI(), true));
            Providers.Add("poloniex", new ExchangeSharpRateProvider("poloniex", new ExchangePoloniexAPI(), true));
            Providers.Add("hitbtc", new ExchangeSharpRateProvider("hitbtc", new ExchangeHitBTCAPI(), true));
            Providers.Add("ndax", new ExchangeSharpRateProvider("ndax", new ExchangeNDAXAPI(), true));

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
                if (provider.Key == CoinGeckoRateProvider.CoinGeckoName)
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
            foreach (var supportedExchange in GetCoinGeckoSupportedExchanges())
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
            foreach (var supportedExchange in GetCoinAverageSupportedExchanges())
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
                foreach (var exchange in GetCoinAverageSupportedExchanges())
                {
                    availableProviders.TryAdd(exchange.Id, exchange);
                }
                _AvailableRateProviders = availableProviders.Values.OrderBy(o => o.Name).ToArray();
            }
            return _AvailableRateProviders;
        }

        internal IEnumerable<AvailableRateProvider> GetCoinAverageSupportedExchanges()
        {
            foreach (var item in
             new[] {
                (DisplayName: "Idex", Name: "idex"),
                (DisplayName: "Coinfloor", Name: "coinfloor"),
                (DisplayName: "Okex", Name: "okex"),
                (DisplayName: "Bitfinex", Name: "bitfinex"),
                (DisplayName: "Bittylicious", Name: "bittylicious"),
                (DisplayName: "BTC Markets", Name: "btcmarkets"),
                (DisplayName: "Kucoin", Name: "kucoin"),
                (DisplayName: "IDAX", Name: "idax"),
                (DisplayName: "Kraken", Name: "kraken"),
                (DisplayName: "Bit2C", Name: "bit2c"),
                (DisplayName: "Mercado Bitcoin", Name: "mercado"),
                (DisplayName: "CEX.IO", Name: "cex"),
                (DisplayName: "Bitex.la", Name: "bitex"),
                (DisplayName: "Quoine", Name: "quoine"),
                (DisplayName: "Stex", Name: "stex"),
                (DisplayName: "CoinTiger", Name: "cointiger"),
                (DisplayName: "Poloniex", Name: "poloniex"),
                (DisplayName: "Zaif", Name: "zaif"),
                (DisplayName: "Huobi", Name: "huobi"),
                (DisplayName: "QuickBitcoin", Name: "quickbitcoin"),
                (DisplayName: "Tidex", Name: "tidex"),
                (DisplayName: "Tokenomy", Name: "tokenomy"),
                (DisplayName: "Bitcoin.co.id", Name: "bitcoin_co_id"),
                (DisplayName: "Kryptono", Name: "kryptono"),
                (DisplayName: "Bitso", Name: "bitso"),
                (DisplayName: "Korbit", Name: "korbit"),
                (DisplayName: "Yobit", Name: "yobit"),
                (DisplayName: "BitBargain", Name: "bitbargain"),
                (DisplayName: "Livecoin", Name: "livecoin"),
                (DisplayName: "Hotbit", Name: "hotbit"),
                (DisplayName: "Coincheck", Name: "coincheck"),
                (DisplayName: "Binance", Name: "binance"),
                (DisplayName: "Bit-Z", Name: "bitz"),
                (DisplayName: "Coinbase Pro", Name: "coinbasepro"),
                (DisplayName: "Rock Trading", Name: "rocktrading"),
                (DisplayName: "Bittrex", Name: "bittrex"),
                (DisplayName: "BitBay", Name: "bitbay"),
                (DisplayName: "Tokenize", Name: "tokenize"),
                (DisplayName: "Hitbtc", Name: "hitbtc"),
                (DisplayName: "Upbit", Name: "upbit"),
                (DisplayName: "Bitstamp", Name: "bitstamp"),
                (DisplayName: "Luno", Name: "luno"),
                (DisplayName: "Trade.io", Name: "tradeio"),
                (DisplayName: "LocalBitcoins", Name: "localbitcoins"),
                (DisplayName: "Independent Reserve", Name: "independentreserve"),
                (DisplayName: "Coinsquare", Name: "coinsquare"),
                (DisplayName: "Exmoney", Name: "exmoney"),
                (DisplayName: "Coinegg", Name: "coinegg"),
                (DisplayName: "FYB-SG", Name: "fybsg"),
                (DisplayName: "Cryptonit", Name: "cryptonit"),
                (DisplayName: "BTCTurk", Name: "btcturk"),
                (DisplayName: "bitFlyer", Name: "bitflyer"),
                (DisplayName: "Negocie Coins", Name: "negociecoins"),
                (DisplayName: "OasisDEX", Name: "oasisdex"),
                (DisplayName: "CoinMate", Name: "coinmate"),
                (DisplayName: "BitForex", Name: "bitforex"),
                (DisplayName: "Bitsquare", Name: "bitsquare"),
                (DisplayName: "FYB-SE", Name: "fybse"),
                (DisplayName: "itBit", Name: "itbit"),
                })
            {
                yield return new AvailableRateProvider(item.Name, item.DisplayName, $"https://apiv2.bitcoinaverage.com/exchanges/{item.Name}", RateSource.CoinAverage);
            }
            yield return new AvailableRateProvider("gdax", string.Empty, $"https://apiv2.bitcoinaverage.com/exchanges/gdax", RateSource.CoinAverage);
        }

        internal IEnumerable<AvailableRateProvider> GetCoinGeckoSupportedExchanges()
        {
            return JArray.Parse(CoinGeckoRateProvider.SupportedExchanges).Select(token =>
                    new AvailableRateProvider(Normalize(token["id"].ToString().ToLowerInvariant()), token["name"].ToString(),
                        $"https://api.coingecko.com/api/v3/exchanges/{token["id"]}/tickers", RateSource.Coingecko))
                .Concat(new[] { new AvailableRateProvider("gdax", string.Empty, $"https://api.coingecko.com/api/v3/exchanges/gdax", RateSource.Coingecko) });
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
                Latency = wrapper.Latency,
                ExchangeRates = value,
                Exception = wrapper.Exception != null ? new ExchangeException() { Exception = wrapper.Exception, ExchangeName = exchangeName } : null
            };
        }
    }
}
