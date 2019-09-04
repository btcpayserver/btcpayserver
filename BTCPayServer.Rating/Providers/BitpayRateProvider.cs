using System.Linq;
using System;
using System.Collections.Generic;
using System.Text;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using System.Threading;
using System.Net.Http;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class BitpayRateProvider : IRateProvider, IHasExchangeName
    {
        public const string BitpayName = "bitpay";
        private readonly HttpClient _httpClient;
        public BitpayRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public string ExchangeName => BitpayName;

        public async Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://bitpay.com/rates", cancellationToken);
            var jarray = (JArray)(await response.Content.ReadAsAsync<JObject>(cancellationToken))["data"];
            return new ExchangeRates(jarray
                .Children<JObject>()
                .Select(jobj => new ExchangeRate(ExchangeName, new CurrencyPair("BTC", jobj["code"].Value<string>()), new BidAsk(jobj["rate"].Value<decimal>())))
                .Where(o => o.CurrencyPair.Right != "BTC")
                .ToArray());
        }
    }
}
