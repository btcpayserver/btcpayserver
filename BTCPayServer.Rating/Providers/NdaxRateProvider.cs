using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class NdaxRateProvider : IRateProvider, IHasExchangeName
    {
        private readonly HttpClient _httpClient;

        public NdaxRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public string ExchangeName => "ndax";

        public async Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://ndax.io/api/returnTicker", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<Dictionary<string, JObject>>(cancellationToken);
            return new ExchangeRates(jobj.Select(pair => new ExchangeRate(ExchangeName, CurrencyPair.Parse(pair.Key),
                new BidAsk(GetValue(pair.Value["highestBid"]), GetValue(pair.Value["lowestAsk"])))));
        }

        private static decimal GetValue(JToken jobj)
        {
            return string.IsNullOrEmpty(jobj.ToString()) ? 0 : jobj.Value<decimal>();
        }

    }
}
