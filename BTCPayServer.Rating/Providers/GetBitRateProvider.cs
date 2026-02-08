using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class GetBitRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        
        public RateSourceInfo RateSourceInfo => new("getbit", "GetBit", "https://venus.getbitmoneyapp.com/uat/getBitcoinPriceV2");

        public GetBitRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? throw new ArgumentNullException(nameof(httpClient));
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(RateSourceInfo.Url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            
            var data = jobj["data"];
            if (data == null)
                return Array.Empty<PairRate>();

            var market = data["market"]?.Value<string>();
            var value = data["value"]?.Value<decimal>();

            if (string.IsNullOrEmpty(market) || !value.HasValue)
                return Array.Empty<PairRate>();

            if (CurrencyPair.TryParse(market, out var pair))
            {
                return new[] { new PairRate(pair, new BidAsk(value.Value)) };
            }

            return Array.Empty<PairRate>();
        }
    }
}
