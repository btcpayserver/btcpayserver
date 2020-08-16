using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Rating
{
    public class HitBTCRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        public HitBTCRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://api.hitbtc.com/api/2/public/ticker", cancellationToken);
            var jarray = await response.Content.ReadAsAsync<JArray>(cancellationToken);
            return jarray
                .Children<JObject>()
                .Where(p => CurrencyPair.TryParse(p["symbol"].Value<string>(), out _))
                .Select(p => new PairRate(CurrencyPair.Parse(p["symbol"].Value<string>()), CreateBidAsk(p)))
                .ToArray();
        }

        private BidAsk CreateBidAsk(JObject p)
        {
            var bid = p["bid"].Value<decimal>();
            var ask = p["ask"].Value<decimal>();
            return new BidAsk(bid, ask);
        }
    }
}
