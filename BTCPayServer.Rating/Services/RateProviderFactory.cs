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

            yield return new AvailableRateProvider("bitfinex", "Bitfinex", "https://api.bitfinex.com/v2/tickers?symbols=tBTCUSD,tLTCUSD,tLTCBTC,tETHUSD,tETHBTC,tETCBTC,tETCUSD,tRRTUSD,tRRTBTC,tZECUSD,tZECBTC,tXMRUSD,tXMRBTC,tDSHUSD,tDSHBTC,tBTCEUR,tBTCJPY,tXRPUSD,tXRPBTC,tIOTUSD,tIOTBTC,tIOTETH,tEOSUSD,tEOSBTC,tEOSETH,tSANUSD,tSANBTC,tSANETH,tOMGUSD,tOMGBTC,tOMGETH,tNEOUSD,tNEOBTC,tNEOETH,tETPUSD,tETPBTC,tETPETH,tQTMUSD,tQTMBTC,tQTMETH,tAVTUSD,tAVTBTC,tAVTETH,tEDOUSD,tEDOBTC,tEDOETH,tBTGUSD,tBTGBTC,tDATUSD,tDATBTC,tDATETH,tQSHUSD,tQSHBTC,tQSHETH,tYYWUSD,tYYWBTC,tYYWETH,tGNTUSD,tGNTBTC,tGNTETH,tSNTUSD,tSNTBTC,tSNTETH,tIOTEUR,tBATUSD,tBATBTC,tBATETH,tMNAUSD,tMNABTC,tMNAETH,tFUNUSD,tFUNBTC,tFUNETH,tZRXUSD,tZRXBTC,tZRXETH,tTNBUSD,tTNBBTC,tTNBETH,tSPKUSD,tSPKBTC,tSPKETH,tTRXUSD,tTRXBTC,tTRXETH,tRCNUSD,tRCNBTC,tRCNETH,tRLCUSD,tRLCBTC,tRLCETH,tAIDUSD,tAIDBTC,tAIDETH,tSNGUSD,tSNGBTC,tSNGETH,tREPUSD,tREPBTC,tREPETH,tELFUSD,tELFBTC,tELFETH,tNECUSD,tNECBTC,tNECETH,tBTCGBP,tETHEUR,tETHJPY,tETHGBP,tNEOEUR,tNEOJPY,tNEOGBP,tEOSEUR,tEOSJPY,tEOSGBP,tIOTJPY,tIOTGBP,tIOSUSD,tIOSBTC,tIOSETH,tAIOUSD,tAIOBTC,tAIOETH,tREQUSD,tREQBTC,tREQETH,tRDNUSD,tRDNBTC,tRDNETH,tLRCUSD,tLRCBTC,tLRCETH,tWAXUSD,tWAXBTC,tWAXETH,tDAIUSD,tDAIBTC,tDAIETH,tAGIUSD,tAGIBTC,tAGIETH,tBFTUSD,tBFTBTC,tBFTETH,tMTNUSD,tMTNBTC,tMTNETH,tODEUSD,tODEBTC,tODEETH,tANTUSD,tANTBTC,tANTETH,tDTHUSD,tDTHBTC,tDTHETH,tMITUSD,tMITBTC,tMITETH,tSTJUSD,tSTJBTC,tSTJETH,tXLMUSD,tXLMEUR,tXLMJPY,tXLMGBP,tXLMBTC,tXLMETH,tXVGUSD,tXVGEUR,tXVGJPY,tXVGGBP,tXVGBTC,tXVGETH,tBCIUSD,tBCIBTC,tMKRUSD,tMKRBTC,tMKRETH,tKNCUSD,tKNCBTC,tKNCETH,tPOAUSD,tPOABTC,tPOAETH,tEVTUSD,tLYMUSD,tLYMBTC,tLYMETH,tUTKUSD,tUTKBTC,tUTKETH,tVEEUSD,tVEEBTC,tVEEETH,tDADUSD,tDADBTC,tDADETH,tORSUSD,tORSBTC,tORSETH,tAUCUSD,tAUCBTC,tAUCETH,tPOYUSD,tPOYBTC,tPOYETH,tFSNUSD,tFSNBTC,tFSNETH,tCBTUSD,tCBTBTC,tCBTETH,tZCNUSD,tZCNBTC,tZCNETH,tSENUSD,tSENBTC,tSENETH,tNCAUSD,tNCABTC,tNCAETH,tCNDUSD,tCNDBTC,tCNDETH,tCTXUSD,tCTXBTC,tCTXETH,tPAIUSD,tPAIBTC,tSEEUSD,tSEEBTC,tSEEETH,tESSUSD,tESSBTC,tESSETH,tATMUSD,tATMBTC,tATMETH,tHOTUSD,tHOTBTC,tHOTETH,tDTAUSD,tDTABTC,tDTAETH,tIQXUSD,tIQXBTC,tIQXEOS,tWPRUSD,tWPRBTC,tWPRETH,tZILUSD,tZILBTC,tZILETH,tBNTUSD,tBNTBTC,tBNTETH,tABSUSD,tABSETH,tXRAUSD,tXRAETH,tMANUSD,tMANETH,tBBNUSD,tBBNETH,tNIOUSD,tNIOETH,tDGXUSD,tDGXETH,tVETUSD,tVETBTC,tVETETH,tUTNUSD,tUTNETH,tTKNUSD,tTKNETH,tGOTUSD,tGOTEUR,tGOTETH,tXTZUSD,tXTZBTC,tCNNUSD,tCNNETH,tBOXUSD,tBOXETH,tTRXEUR,tTRXGBP,tTRXJPY,tMGOUSD,tMGOETH,tRTEUSD,tRTEETH,tYGGUSD,tYGGETH,tMLNUSD,tMLNETH,tWTCUSD,tWTCETH,tCSXUSD,tCSXETH,tOMNUSD,tOMNBTC,tINTUSD,tINTETH,tDRNUSD,tDRNETH,tPNKUSD,tPNKETH,tDGBUSD,tDGBBTC,tBSVUSD,tBSVBTC,tBABUSD,tBABBTC,tWLOUSD,tWLOXLM,tVLDUSD,tVLDETH,tENJUSD,tENJETH,tONLUSD,tONLETH,tRBTUSD,tRBTBTC,tUSTUSD,tEUTEUR,tEUTUSD,tGSDUSD,tUDCUSD,tTSDUSD,tPAXUSD,tRIFUSD,tRIFBTC,tPASUSD,tPASETH,tVSYUSD,tVSYBTC,tZRXDAI,tMKRDAI,tOMGDAI,tBTTUSD,tBTTBTC,tBTCUST,tETHUST,tCLOUSD,tCLOBTC,tIMPUSD,tIMPETH,tLTCUST,tEOSUST,tBABUST,tSCRUSD,tSCRETH,tGNOUSD,tGNOETH,tGENUSD,tGENETH,tATOUSD,tATOBTC,tATOETH,tWBTUSD,tXCHUSD,tEUSUSD,tWBTETH,tXCHETH,tEUSETH,tLEOUSD,tLEOBTC,tLEOUST,tLEOEOS,tLEOETH,tASTUSD,tASTETH,tFOAUSD,tFOAETH,tUFRUSD,tUFRETH,tZBTUSD,tZBTUST,tOKBUSD,tUSKUSD,tGTXUSD,tKANUSD,tOKBUST,tOKBETH,tOKBBTC,tUSKUST,tUSKETH,tUSKBTC,tUSKEOS,tGTXUST,tKANUST,tAMPUSD,tALGUSD,tALGBTC,tALGUST,tBTCXCH,tSWMUSD,tSWMETH,tTRIUSD,tTRIETH,tLOOUSD,tLOOETH,tAMPUST,tDUSK:USD,tDUSK:BTC,tUOSUSD,tUOSBTC,tRRBUSD,tRRBUST,tDTXUSD,tDTXUST,tAMPBTC,tFTTUSD,tFTTUST,tPAXUST,tUDCUST,tTSDUST,tBTC:CNHT,tUST:CNHT,tCNH:CNHT,tCHZUSD,tCHZUST,tBTCF0:USTF0,tETHF0:USTF0");
            yield return new AvailableRateProvider("okex", "OKEx", "https://www.okex.com/api/futures/v3/instruments/ticker");
            yield return new AvailableRateProvider("coinbasepro", "Coinbase Pro", "https://api.pro.coinbase.com/products");
        }
        void InitExchanges()
        {
            // We need to be careful to only add exchanges which OnGetTickers implementation make only 1 request
            AddExchangeSharpProviders<ExchangeBinanceAPI>("binance");
            AddExchangeSharpProviders<ExchangeBittrexAPI>("bittrex");
            AddExchangeSharpProviders<ExchangePoloniexAPI>("poloniex");
            AddExchangeSharpProviders<ExchangeHitBTCAPI>("hitbtc");
            AddExchangeSharpProviders<ExchangeNDAXAPI>("ndax");

            // Handmade providers
            Providers.Add("coingecko", new CoinGeckoRateProvider(_httpClientFactory));
            Providers.Add("kraken", new KrakenExchangeRateProvider() { HttpClient = _httpClientFactory?.CreateClient("EXCHANGE_KRAKEN") });
            Providers.Add("bylls", new ByllsRateProvider(_httpClientFactory?.CreateClient("EXCHANGE_BYLLS")));
            Providers.Add("bitbank", new BitbankRateProvider(_httpClientFactory?.CreateClient("EXCHANGE_BITBANK")));
            Providers.Add("bitpay", new BitpayRateProvider(_httpClientFactory?.CreateClient("EXCHANGE_BITPAY")));


            // Backward compatibility: coinaverage should be using coingecko to prevent stores from breaking
            Providers.Add("coinaverage", new CoinGeckoRateProvider(_httpClientFactory));

            AddExchangeSharpProviders<ExchangeBitfinexAPI>("bitfinex");
            AddExchangeSharpProviders<ExchangeOKExAPI>("okex");
            AddExchangeSharpProviders<ExchangeCoinbaseAPI>("coinbasepro");
            // Those exchanges make too many requests, exchange sharp do not parallelize so it is too slow...
            //AddExchangeSharpProviders<ExchangeGeminiAPI>("gemini");
            //AddExchangeSharpProviders<ExchangeBitstampAPI>("bitstamp");
            //AddExchangeSharpProviders<ExchangeBitMEXAPI>("bitmex");

            foreach (var provider in Providers.ToArray())
            {
                var prov = new BackgroundFetcherRateProvider(Providers[provider.Key]);
                prov.RefreshRate = TimeSpan.FromMinutes(1.0);
                prov.ValidatyTime = TimeSpan.FromMinutes(5.0);
                Providers[provider.Key] = prov;
            }
            Providers["gdax"] = Providers["coinbasepro"];

            foreach (var supportedExchange in GetCoinGeckoSupportedExchanges())
            {
                if (!Providers.ContainsKey(supportedExchange.Id) && supportedExchange.Id != CoinGeckoRateProvider.CoinGeckoName)
                {
                    var coingecko = new CoinGeckoRateProvider(_httpClientFactory)
                    {
                        UnderlyingExchange = supportedExchange.SourceId
                    };
                    var bgFetcher = new BackgroundFetcherRateProvider(coingecko);
                    bgFetcher.RefreshRate = TimeSpan.FromMinutes(1.0);
                    bgFetcher.ValidatyTime = TimeSpan.FromMinutes(5.0);
                    Providers.Add(supportedExchange.Id, bgFetcher);
                }
            }
        }

        private IRateProvider AddExchangeSharpProviders<T>(string providerName) where T: ExchangeAPI, new()
        {
            var provider = new ExchangeSharpRateProvider<T>(_httpClientFactory.CreateClient($"EXCHANGE_{providerName}".ToUpperInvariant()), true);
            Providers.Add(providerName, provider);
            return provider;
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
                        $"https://api.coingecko.com/api/v3/exchanges/{token["id"]}/tickers", RateSource.Coingecko));
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
