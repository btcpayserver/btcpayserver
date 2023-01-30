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
        public RateSourceInfo RateSourceInfo => new("hitbtc", "HitBTC", "https://api.hitbtc.com/api/2/public/ticker");
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
                .Select(p =>
                {
                    CurrencyPair.TryParse(p["symbol"].Value<string>(), out var currency);
                    var bidask = CreateBidAsk(p);
                    return (currency, bidask);
                })
                .Where(p => p.currency != null && p.bidask != null)
                .Select(p => new PairRate(p.currency, p.bidask))
                .ToArray();
        }

        private BidAsk CreateBidAsk(JObject p)
        {
            if (p["bid"].Type != JTokenType.String || p["ask"].Type != JTokenType.String)
                return null;
            var bid = p["bid"].Value<decimal>();
            var ask = p["ask"].Value<decimal>();
            return new BidAsk(bid, ask);
        }
    }
}
