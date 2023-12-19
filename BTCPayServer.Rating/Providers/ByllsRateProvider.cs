using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class ByllsRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        public ByllsRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public RateSourceInfo RateSourceInfo => new RateSourceInfo("bylls", "Bylls", "https://bylls.com/api/price?from_currency=BTC&to_currency=CAD");

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync("https://bylls.com/api/price?from_currency=BTC&to_currency=CAD", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            var value = jobj["public_price"]["to_price"].Value<decimal>();
            return new[] { new PairRate(new CurrencyPair("BTC", "CAD"), new BidAsk(value)) };
        }
    }
}
