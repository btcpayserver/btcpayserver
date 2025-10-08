using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class YadioRateProvider : IRateProvider
    {
        public RateSourceInfo RateSourceInfo => new("yadio", "Yadio", "https://api.yadio.io/exrates/BTC");
        private readonly HttpClient _httpClient;
        public YadioRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync("https://api.yadio.io/exrates/BTC", cancellationToken);
            response.EnsureSuccessStatusCode();
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            var results = jobj["BTC"];
            var list = new List<PairRate>();
            foreach (var item in results)
            {
                string name = ((JProperty)item).Name;
                var value = results[name].Value<decimal?>();
                if (value.HasValue)
                    list.Add(new PairRate(new CurrencyPair("BTC", name), new BidAsk(value.Value)));
            }

            return list.ToArray();
        }
    }
}
