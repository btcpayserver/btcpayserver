using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class BareBitcoinRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        
        public RateSourceInfo RateSourceInfo => new("barebitcoin", "Bare Bitcoin", "https://api.bb.no/v1/price/nok");

        public BareBitcoinRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(RateSourceInfo.Url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            
            // Extract bid/ask prices from JSON response
            var bid = (decimal)jobj["bid"];
            var ask = (decimal)jobj["ask"];

            // Create currency pair for BTC/NOK
            var pair = new CurrencyPair("BTC", "NOK");
            
            // Return single pair rate with bid/ask
            return new[] { new PairRate(pair, new BidAsk(bid, ask)) };
        }
    }
}