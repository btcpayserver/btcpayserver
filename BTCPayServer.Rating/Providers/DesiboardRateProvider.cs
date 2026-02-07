using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class DesiboardRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;
        
        public RateSourceInfo RateSourceInfo => new("desiboard", "Desiboard", "https://desiboard.thevikas.com/api/price");

        public DesiboardRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync(RateSourceInfo.Url, cancellationToken);
            response.EnsureSuccessStatusCode();
            
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            
            var btcusd = jobj["BTCUSD"]?.Value<decimal>();
            var btcinr = jobj["BTCINR"]?.Value<decimal>();

            var rates = new System.Collections.Generic.List<PairRate>();

            if (btcusd.HasValue)
            {
                var usdPair = new CurrencyPair("BTC", "USD");
                rates.Add(new PairRate(usdPair, new BidAsk(btcusd.Value)));
            }

            if (btcinr.HasValue)
            {
                var inrPair = new CurrencyPair("BTC", "INR");
                rates.Add(new PairRate(inrPair, new BidAsk(btcinr.Value)));
            }

            return rates.ToArray();
        }
    }
}
