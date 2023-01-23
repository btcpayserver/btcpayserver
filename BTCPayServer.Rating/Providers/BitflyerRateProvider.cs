using System;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class BitflyerRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        public BitflyerRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public RateSourceInfo RateSourceInfo => new RateSourceInfo("bitflyer", "Bitflyer", "https://api.bitflyer.com/v1/ticker");

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://api.bitflyer.jp/v1/ticker", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            if (jobj.Property("error_message")?.Value?.Value<string>() is string err)
            {
                throw new Exception($"Error from bitflyer: {err}");
            }
            var bid = jobj.Property("best_bid").Value.Value<decimal>();
            var ask = jobj.Property("best_ask").Value.Value<decimal>();
            var rates = new PairRate[1];
            rates[0] = new PairRate(CurrencyPair.Parse(jobj.Property("product_code").Value.Value<string>()), new BidAsk(bid, ask));
            return rates;
        }
    }
}
