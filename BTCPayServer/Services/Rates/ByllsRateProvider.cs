using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class ByllsRateProvider : IRateProvider, IHasExchangeName
    {
        private readonly HttpClient _httpClient;
        public ByllsRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }
        public string ExchangeName => "bylls";

        public async Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://bylls.com/api/price?from_currency=BTC&to_currency=CAD", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            var value = jobj["public_price"]["to_price"].Value<decimal>();
            return new ExchangeRates(new[] { new ExchangeRate(ExchangeName, new CurrencyPair("BTC", "CAD"), new BidAsk(value)) });
        }
    }
}
