using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Rating.Providers
{
    public class BitnobRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        public BitnobRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }
        public RateSourceInfo RateSourceInfo => new("bitnob", "Bitnob", "https://api.bitnob.co/api/v1/rates/bitcoin/price");

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync("https://api.bitnob.co/api/v1/rates/bitcoin/price", cancellationToken);
            JObject jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            var dataObject = jobj["data"] as JObject;

            if (dataObject == null)
            {
                return Array.Empty<PairRate>();
            }
            var pairRates = new List<PairRate>();
            foreach (var property in dataObject.Properties())
            {
                string[] parts = property.Name.Split('_');
                decimal value = property.Value.Value<decimal>();
                // When API is broken, they return 0 rate
                if (value != 0m)
                    pairRates.Add(new PairRate(new CurrencyPair("BTC", parts[1]), new BidAsk(value)));
            }
            return pairRates.ToArray();
        }
    }
}
