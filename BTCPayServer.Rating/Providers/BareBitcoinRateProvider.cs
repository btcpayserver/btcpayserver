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
        
        public RateSourceInfo RateSourceInfo => new("barebitcoin", "Bare Bitcoin", "https://api.bb.no/price");

        public BareBitcoinRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(RateSourceInfo.Url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            
            // Extract market and otc prices
            var market = jobj["market"].Value<decimal>();
            var buy = jobj["buy"].Value<decimal>();
            var sell = jobj["sell"].Value<decimal>();

            // Create currency pair for BTC/NOK
            var pair = new CurrencyPair("BTC", "NOK");
            
            // Return single pair rate with sell/buy as bid/ask
            return new[] { new PairRate(pair, new BidAsk(sell, buy)) };
        }
    }
}