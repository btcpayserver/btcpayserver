using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class BitcoinKenyaRateProvider : IRateProvider
    {
        public RateSourceInfo RateSourceInfo => new("bitcoinkenya", "Bitcoin.co.ke", "https://trex.bitcoin.co.ke/btcpay/rates");
        private readonly HttpClient _httpClient;

        public BitcoinKenyaRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var request = new HttpRequestMessage(HttpMethod.Get, RateSourceInfo.Url);
            request.Headers.UserAgent.ParseAdd("BTCPay-BitcoinKenya/1.0");
            using var response = await _httpClient.SendAsync(request, cancellationToken);
            response.EnsureSuccessStatusCode();
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            if (jobj["BTC"] is not JObject results)
                return Array.Empty<PairRate>();
            var list = new List<PairRate>();
            foreach (var prop in results.Properties())
            {
                var value = prop.Value.Value<decimal?>();
                if (value.HasValue && value.Value > 0m)
                    list.Add(new PairRate(new CurrencyPair("BTC", prop.Name), new BidAsk(value.Value)));
            }
            return list.ToArray();
        }
    }
}
