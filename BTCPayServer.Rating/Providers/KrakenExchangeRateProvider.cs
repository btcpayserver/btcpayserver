using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using ExchangeSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    // Make sure that only one request is sent to kraken in general
    public class KrakenExchangeRateProvider : IRateProvider
    {
        public KrakenExchangeRateProvider()
        {
            _Helper = new ExchangeKrakenAPI();
        }
        ExchangeKrakenAPI _Helper;
        public HttpClient HttpClient
        {
            get
            {
                return _LocalClient ?? _Client;
            }
            set
            {
                _LocalClient = value;
            }
        }

        HttpClient _LocalClient;
        static HttpClient _Client = new HttpClient();

        // ExchangeSymbolToGlobalSymbol throws exception which would kill perf
        ConcurrentDictionary<string, string> notFoundSymbols = new ConcurrentDictionary<string, string>(new Dictionary<string, string>()
        {
            {"ADAXBT","ADAXBT"},
            { "BSVUSD","BSVUSD"},
            { "QTUMEUR","QTUMEUR"},
            { "QTUMXBT","QTUMXBT"},
            { "EOSUSD","EOSUSD"},
            { "XTZUSD","XTZUSD"},
            { "XREPZUSD","XREPZUSD"},
            { "ADAEUR","ADAEUR"},
            { "ADAUSD","ADAUSD"},
            { "GNOEUR","GNOEUR"},
            { "XTZETH","XTZETH"},
            { "XXRPZJPY","XXRPZJPY"},
            { "XXRPZCAD","XXRPZCAD"},
            { "XTZEUR","XTZEUR"},
            { "QTUMETH","QTUMETH"},
            { "XXLMZUSD","XXLMZUSD"},
            { "QTUMCAD","QTUMCAD"},
            { "QTUMUSD","QTUMUSD"},
            { "XTZXBT","XTZXBT"},
            { "GNOUSD","GNOUSD"},
            { "ADAETH","ADAETH"},
            { "ADACAD","ADACAD"},
            { "XTZCAD","XTZCAD"},
            { "BSVEUR","BSVEUR"},
            { "XZECZJPY","XZECZJPY"},
            { "XXLMZEUR","XXLMZEUR"},
            {"EOSEUR","EOSEUR"},
            {"BSVXBT","BSVXBT"}
        });
        string[] _Symbols = Array.Empty<string>();
        DateTimeOffset? _LastSymbolUpdate = null;


        Dictionary<string, string> _TickerMapping = new Dictionary<string, string>()
        {
            { "XXDG", "DOGE" },
            { "XXBT", "BTC" },
            { "XBT", "BTC" },
            { "DASH", "DASH" },
            { "ZUSD", "USD" },
            { "ZEUR", "EUR" },
            { "ZJPY", "JPY" },
            { "ZCAD", "CAD" },
            { "ZGBP", "GBP" }
        };

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var result = new List<PairRate>();
            var symbols = await GetSymbolsAsync(cancellationToken);
            var normalizedPairsList = symbols.Where(s => !notFoundSymbols.ContainsKey(s)).Select(s => _Helper.NormalizeMarketSymbol(s)).ToList();
            var csvPairsList = string.Join(",", normalizedPairsList);
            JToken apiTickers = await MakeJsonRequestAsync<JToken>("/0/public/Ticker", null, new Dictionary<string, object> { { "pair", csvPairsList } }, cancellationToken: cancellationToken);
            var tickers = new List<KeyValuePair<string, ExchangeTicker>>();
            foreach (string symbol in symbols)
            {
                var ticker = ConvertToExchangeTicker(symbol, apiTickers[symbol]);
                if (ticker != null)
                {
                    try
                    {
                        string global = null;
                        var mapped1 = _TickerMapping.Where(t => symbol.StartsWith(t.Key, StringComparison.OrdinalIgnoreCase))
                                                   .Select(t => new { KrakenTicker = t.Key, PayTicker = t.Value }).SingleOrDefault();
                        if (mapped1 != null)
                        {
                            var p2 = symbol.Substring(mapped1.KrakenTicker.Length);
                            if (_TickerMapping.TryGetValue(p2, out var mapped2))
                                p2 = mapped2;
                            global = $"{p2}_{mapped1.PayTicker}";
                        }
                        else
                        {
                            global = await _Helper.ExchangeMarketSymbolToGlobalMarketSymbolAsync(symbol);
                        }
                        if (CurrencyPair.TryParse(global, out var pair))
                            result.Add(new PairRate(pair.Inverse(), new BidAsk(ticker.Bid, ticker.Ask)));
                        else
                            notFoundSymbols.TryAdd(symbol, symbol);
                    }
                    catch (ArgumentException)
                    {
                        notFoundSymbols.TryAdd(symbol, symbol);
                    }
                }
            }
            return result.ToArray();
        }

        private static ExchangeTicker ConvertToExchangeTicker(string symbol, JToken ticker)
        {
            if (ticker == null)
                return null;
            decimal last = ticker["c"][0].ConvertInvariant<decimal>();
            return new ExchangeTicker
            {
                Ask = ticker["a"][0].ConvertInvariant<decimal>(),
                Bid = ticker["b"][0].ConvertInvariant<decimal>(),
                Last = last,
                Volume = new ExchangeVolume
                {
                    BaseCurrencyVolume = ticker["v"][1].ConvertInvariant<decimal>(),
                    BaseCurrency = symbol,
                    QuoteCurrencyVolume = ticker["v"][1].ConvertInvariant<decimal>() * last,
                    QuoteCurrency = symbol,
                    Timestamp = DateTime.UtcNow
                }
            };
        }

        private async Task<string[]> GetSymbolsAsync(CancellationToken cancellationToken)
        {
            if (_LastSymbolUpdate != null && DateTimeOffset.UtcNow - _LastSymbolUpdate.Value < TimeSpan.FromDays(0.5))
            {
                return _Symbols;
            }
            else
            {
                JToken json = await MakeJsonRequestAsync<JToken>("/0/public/AssetPairs", cancellationToken: cancellationToken);
                var symbols = (from prop in json.Children<JProperty>() where !prop.Name.Contains(".d", StringComparison.OrdinalIgnoreCase) select prop.Name).ToArray();
                _Symbols = symbols;
                _LastSymbolUpdate = DateTimeOffset.UtcNow;
                return symbols;
            }
        }

        private async Task<T> MakeJsonRequestAsync<T>(string url, string baseUrl = null, Dictionary<string, object> payload = null, string requestMethod = null, CancellationToken cancellationToken = default)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("https://api.kraken.com");
            ;
            sb.Append(url);
            if (payload != null)
            {
                sb.Append("?");
                sb.Append(String.Join('&', payload.Select(kv => $"{kv.Key}={kv.Value}").OfType<object>().ToArray()));
            }
            var request = new HttpRequestMessage(HttpMethod.Get, sb.ToString());
            var response = await HttpClient.SendAsync(request, cancellationToken);
            string stringResult = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<T>(stringResult);
            if (result is JToken json)
            {
                if (!(json is JArray) && json["error"] is JArray error && error.Count != 0)
                {
                    throw new APIException(error[0].ToStringInvariant());
                }
                result = (T)(object)(json["result"] ?? json);
            }
            return result;
        }
    }
}
