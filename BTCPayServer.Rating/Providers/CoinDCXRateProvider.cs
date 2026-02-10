using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class CoinDCXRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        
        public RateSourceInfo RateSourceInfo => new("coindcx", "CoinDCX", "https://api.coindcx.com/exchange/ticker");

        public CoinDCXRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(RateSourceInfo.Url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var jarray = await response.Content.ReadAsAsync<JArray>(cancellationToken);
            
            var rates = new System.Collections.Generic.List<PairRate>();

            foreach (var item in jarray)
            {
                var market = item["market"]?.Value<string>();
                if (string.IsNullOrEmpty(market))
                    continue;

                if (CurrencyPair.TryParse(market, out var pair))
                {
                    var bid = item["bid"]?.Value<decimal>();
                    var ask = item["ask"]?.Value<decimal>();

                    if (bid.HasValue && ask.HasValue && bid.Value > 0 && ask.Value > 0)
                    {
                        rates.Add(new PairRate(pair, new BidAsk(bid.Value, ask.Value)));
                    }
                }
            }

            return rates.ToArray();
        }
    }
}
