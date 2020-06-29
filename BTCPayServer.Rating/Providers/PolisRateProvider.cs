using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class PolisRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        public PolisRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://obol.polispay.com/complex/btc/polis", cancellationToken);  //Returns complex rate from BTC to POLIS
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            var value = jobj["data"].Value<decimal>();
            return new[] { new PairRate(new CurrencyPair("BTC", "POLIS"), new BidAsk(value)) };
        }
    }
}
