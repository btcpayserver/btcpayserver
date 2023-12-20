using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Rating.Providers
{
    public class BtcTurkRateProvider : IRateProvider
    {
        class Ticker
        {
            public string pairNormalized { get; set; }
            public decimal? bid { get; set; }
            public decimal? ask { get; set; }
        }
        private readonly HttpClient _httpClient;

        public RateSourceInfo RateSourceInfo => new RateSourceInfo("btcturk", "BtcTurk", "https://api.btcturk.com/api/v2/ticker");

        public BtcTurkRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync("https://api.btcturk.com/api/v2/ticker", cancellationToken);
            var jarray = (JArray)(await response.Content.ReadAsAsync<JObject>(cancellationToken))["data"];
            var tickers = jarray.ToObject<Ticker[]>();
            return tickers
                .Where(t => t.bid is not null && t.ask is not null)
                .Select(t => new PairRate(CurrencyPair.Parse(t.pairNormalized), new BidAsk(t.bid.Value, t.ask.Value))).ToArray();
        }
    }
}
