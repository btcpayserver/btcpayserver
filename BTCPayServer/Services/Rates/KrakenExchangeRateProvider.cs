using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using ExchangeSharp;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    // Make sure that only one request is sent to kraken in general
    public class KrakenExchangeRateProvider : IRateProvider, IHasExchangeName
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
                _LocalClient = null;
            }
        }

        public string ExchangeName => "kraken";

        HttpClient _LocalClient;
        static HttpClient _Client = new HttpClient();

        // ExchangeSymbolToGlobalSymbol throws exception which would kill perf
        ConcurrentDictionary<string, string> notFoundSymbols = new ConcurrentDictionary<string, string>();
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
        };

        public async Task<ExchangeRates> GetRatesAsync()
        {
            var result = new ExchangeRates();
            var symbols = await GetSymbolsAsync();
            var normalizedPairsList = symbols.Where(s => !notFoundSymbols.ContainsKey(s)).Select(s => _Helper.NormalizeSymbol(s)).ToList();
            var csvPairsList = string.Join(",", normalizedPairsList);
            JToken apiTickers = await MakeJsonRequestAsync<JToken>("/0/public/Ticker", null, new Dictionary<string, object> { { "pair", csvPairsList } });
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
                            global = _Helper.ExchangeSymbolToGlobalSymbol(symbol);
                        }
                        if (CurrencyPair.TryParse(global, out var pair))
                            result.Add(new ExchangeRate("kraken", pair.Inverse(), new BidAsk(ticker.Bid, ticker.Ask)));
                        else
                            notFoundSymbols.TryAdd(symbol, symbol);
                    }
                    catch (ArgumentException)
                    {
                        notFoundSymbols.TryAdd(symbol, symbol);
                    }
                }
            }
            return result;
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
                    BaseVolume = ticker["v"][1].ConvertInvariant<decimal>(),
                    BaseSymbol = symbol,
                    ConvertedVolume = ticker["v"][1].ConvertInvariant<decimal>() * last,
                    ConvertedSymbol = symbol,
                    Timestamp = DateTime.UtcNow
                }
            };
        }

        private async Task<string[]> GetSymbolsAsync()
        {
            if (_LastSymbolUpdate != null && DateTimeOffset.UtcNow - _LastSymbolUpdate.Value < TimeSpan.FromDays(0.5))
            {
                return _Symbols;
            }
            else
            {
                JToken json = await MakeJsonRequestAsync<JToken>("/0/public/AssetPairs");
                var symbols = (from prop in json.Children<JProperty>() where !prop.Name.Contains(".d", StringComparison.OrdinalIgnoreCase) select prop.Name).ToArray();
                _Symbols = symbols;
                _LastSymbolUpdate = DateTimeOffset.UtcNow;
                return symbols;
            }
        }

        private async Task<T> MakeJsonRequestAsync<T>(string url, string baseUrl = null, Dictionary<string, object> payload = null, string requestMethod = null)
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
            var response = await HttpClient.SendAsync(request);
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
