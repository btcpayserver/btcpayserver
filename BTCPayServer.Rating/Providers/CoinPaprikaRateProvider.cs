using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class CoinPaprikaRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;

        private static readonly Dictionary<string, string> SupportedCoins = new()
        {
            ["LCC"] = "lcc-litecoin-cash"
        };

        public RateSourceInfo RateSourceInfo =>
            new("coinpaprika", "CoinPaprika", "https://api.coinpaprika.com/");

        public CoinPaprikaRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var rates = new List<PairRate>();

            foreach (var coin in SupportedCoins)
            {
                using var response = await _httpClient.GetAsync(
                    $"https://api.coinpaprika.com/v1/tickers/{coin.Value}?quotes=BTC",
                    cancellationToken);

                response.EnsureSuccessStatusCode();

                var json = await response.Content.ReadAsAsync<JObject>(cancellationToken);

                var symbol = json["symbol"]?.Value<string>();
                var price = json["quotes"]?["BTC"]?["price"]?.Value<decimal?>();

                if (symbol == coin.Key && price is > 0m)
                {
                    rates.Add(
                        new PairRate(
                            new CurrencyPair(symbol, "BTC"),
                            new BidAsk(price.Value)));
                }
            }

            return rates.ToArray();
        }
    }
}
