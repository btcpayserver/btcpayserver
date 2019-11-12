﻿using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class PolisRateProvider : IRateProvider, IHasExchangeName
    {
        private readonly HttpClient _httpClient;
        public PolisRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }
        public string ExchangeName => "polispay";

        public async Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://obol.polispay.com/complex/btc/polis", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            var value = jobj["data"].Value<decimal>();
            return new ExchangeRates(new[] { new ExchangeRate(ExchangeName, new CurrencyPair("POLIS", "BTC"), new BidAsk(value)) });
        }
    }
}
