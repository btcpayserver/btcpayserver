using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class NotbankExchangeRateProvider : IRateProvider
    {
        public RateSourceInfo RateSourceInfo => new("notbank", "Notbank", "https://api.notbank.com/AP/Ticker");
        private readonly HttpClient _httpClient;

        public NotbankExchangeRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }
        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.PostAsync("https://api.notbank.com/AP/Ticker", new StringContent("{}", Encoding.UTF8, "application/json"), cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);

            return ((jobj as JObject) ?? new JObject())
                .Properties()
                .Select(p => CreatePairRate(p))
                .Where(p => p is not null)
                .Select(p => p!)
                .ToArray();
        }

        private PairRate CreatePairRate(JProperty p)
        {
            if (!CurrencyPair.TryParse(p.Name, out var pair))
                return null;
            var lastPrice = p.Value["last_price"]?.Value<string>();
            if (lastPrice is null || !decimal.TryParse(lastPrice, NumberStyles.Any, CultureInfo.InvariantCulture, out var lastPricev))
                return null;
            return new PairRate(pair, new BidAsk(lastPricev));
        }
    }
}
