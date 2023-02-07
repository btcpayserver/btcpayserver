using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class CryptoMarketExchangeRateProvider : IRateProvider
    {
        public RateSourceInfo RateSourceInfo => new("cryptomarket", "CryptoMarket", "https://api.exchange.cryptomkt.com/api/3/public/ticker/");
        private readonly HttpClient _httpClient;
        public CryptoMarketExchangeRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }


        readonly List<string> SupportedPairs = new List<string>()
        {
            "BTCARS",
            "BTCCLP",
            "BTCBRL"
        };

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://api.exchange.cryptomkt.com/api/3/public/ticker/", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);

            return ((jobj as JObject) ?? new JObject())
                .Properties()
                .Where(p => SupportedPairs.Contains(p.Name))
                .Select(p => new PairRate(CurrencyPair.Parse(p.Name), CreateBidAsk(p)))
                .ToArray();
        }
        private static BidAsk CreateBidAsk(JProperty p)
        {
            var bid = decimal.Parse(p.Value["bid"].Value<string>(), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture);
            var ask = decimal.Parse(p.Value["ask"].Value<string>(), System.Globalization.NumberStyles.Any, CultureInfo.InvariantCulture);
            if (bid > ask)
                return null;
            return new BidAsk(bid, ask);
        }
    }
}
