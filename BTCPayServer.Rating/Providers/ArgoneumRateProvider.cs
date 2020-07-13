using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class ArgoneumRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        public ArgoneumRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            // Example result: AGM to BTC rate: {"agm":5000000.000000}
            var response = await _httpClient.GetAsync("https://rates.argoneum.net/rates/btc", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            var value = jobj["agm"].Value<decimal>();
            return new[] { new PairRate(new CurrencyPair("BTC", "AGM"), new BidAsk(value)) };
        }
    }
}
