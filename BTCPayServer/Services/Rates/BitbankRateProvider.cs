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
    public class BitbankRateProvider : IRateProvider, IHasExchangeName
    {
        private readonly HttpClient _httpClient;
        public BitbankRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }
        public string ExchangeName => "bitbank";

        public async Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://public.bitbank.cc/prices", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            return new ExchangeRates(((jobj["data"] as JObject) ?? new JObject())
                .Properties()
                .Select(p => new ExchangeRate(ExchangeName, CurrencyPair.Parse(p.Name), new BidAsk(p.Value["buy"].Value<decimal>(), p.Value["sell"].Value<decimal>())))
                .ToArray());
                
        }
    }
}
