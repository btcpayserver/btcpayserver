using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class BitmyntRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        
        public RateSourceInfo RateSourceInfo => new("bitmynt", "Bitmynt", "https://ny.bitmynt.no/data/rates.json");

        public BitmyntRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(RateSourceInfo.Url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            
            // Extract bid and ask prices from current_rate object
            var currentRate = jobj["current_rate"];
            var bid = currentRate["bid"].Value<decimal>();
            var ask = currentRate["ask"].Value<decimal>();

            // Create currency pair for BTC/NOK
            var pair = new CurrencyPair("BTC", "NOK");
            
            // Return single pair rate with bid/ask
            return new[] { new PairRate(pair, new BidAsk(bid, ask)) };
        }
    }
}