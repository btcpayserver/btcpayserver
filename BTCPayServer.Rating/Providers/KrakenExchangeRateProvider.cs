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
using Microsoft.CodeAnalysis;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    // Make sure that only one request is sent to kraken in general
    public class KrakenExchangeRateProvider : IRateProvider
    {
        public RateSourceInfo RateSourceInfo => new("kraken", "Kraken", "https://api.kraken.com/0/public/Ticker");
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
        static readonly HttpClient _Client = new HttpClient();
        string[] _Symbols = Array.Empty<string>();
        DateTimeOffset? _LastSymbolUpdate = null;
        readonly Dictionary<string, string> _TickerMapping = new Dictionary<string, string>()
        {
            { "XXDG", "DOGE" },
            { "XXBT", "BTC" },
            { "XBT", "BTC" },
            { "DASH", "DASH" },
            { "ZUSD", "USD" },
            { "ZEUR", "EUR" },
            { "ZJPY", "JPY" },
            { "ZCAD", "CAD" },
            { "ZGBP", "GBP" },
            { "XXMR", "XMR" },
            { "XETH", "ETH" },
            { "USDC", "USDC" }, // On A=A purpose
            { "XZEC", "ZEC" },
            { "XLTC", "LTC" },
            { "XXRP", "XRP" },
        };

        string Normalize(string ticker)
        {
            _TickerMapping.TryGetValue(ticker, out var normalized);
            return normalized ?? ticker;
        }

        readonly ConcurrentDictionary<string, CurrencyPair> CachedCurrencyPairs = new ConcurrentDictionary<string, CurrencyPair>();
        private CurrencyPair GetCurrencyPair(string symbol)
        {
            if (CachedCurrencyPairs.TryGetValue(symbol, out var pair))
                return pair;
            var found = _TickerMapping.Where(t => symbol.StartsWith(t.Key, StringComparison.OrdinalIgnoreCase))
                                                .Select(t => new { KrakenTicker = t.Key, PayTicker = t.Value }).FirstOrDefault();
            if (found is not null)
            {
                pair = new CurrencyPair(found.PayTicker, Normalize(symbol.Substring(found.KrakenTicker.Length)));
            }
            if (pair is null)
            {
                found = _TickerMapping.Where(t => symbol.EndsWith(t.Key, StringComparison.OrdinalIgnoreCase))
                                                    .Select(t => new { KrakenTicker = t.Key, PayTicker = t.Value }).FirstOrDefault();
                if (found is not null)
                    pair = new CurrencyPair(Normalize(symbol.Substring(0, symbol.Length - found.KrakenTicker.Length)), found.PayTicker);
            }
            if (pair is null)
                CurrencyPair.TryParse(symbol, out pair);
            CachedCurrencyPairs.TryAdd(symbol, pair);
            return pair;
        }
        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var result = new List<PairRate>();
            var symbols = await GetSymbolsAsync(cancellationToken);
            JToken apiTickers = await MakeJsonRequestAsync<JToken>("/0/public/Ticker", null, null, cancellationToken: cancellationToken);
            foreach (string symbol in symbols)
            {
                var ticker = ConvertToExchangeTicker(symbol, apiTickers[symbol]);
                if (ticker != null)
                {
                    var pair = GetCurrencyPair(symbol);
                    if (pair is not null && ticker.Bid <= ticker.Ask)
                        result.Add(new PairRate(pair, new BidAsk(ticker.Bid, ticker.Ask)));
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
                sb.Append('?');
                sb.Append(String.Join('&', payload.Select(kv => $"{kv.Key}={kv.Value}").OfType<object>().ToArray()));
            }
            var request = new HttpRequestMessage(HttpMethod.Get, sb.ToString());
            using var response = await HttpClient.SendAsync(request, cancellationToken);
            string stringResult = await response.Content.ReadAsStringAsync();
            var result = JsonConvert.DeserializeObject<T>(stringResult);
            if (result is JToken json)
            {
                if (!(json is JArray) && json["result"] is JObject { Count: > 0 } pairResult)
                {
                    return (T)(object)(pairResult);
                }

                if (!(json is JArray) && json["error"] is JArray error && error.Count != 0)
                {
                    throw new APIException(string.Join("\n",
                        error.Select(token => token.ToStringInvariant()).Distinct()));
                }
                result = (T)(object)(json["result"] ?? json);
            }
            return result;
        }
    }
}
