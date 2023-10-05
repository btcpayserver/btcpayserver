using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Text;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class BitcoinJungleRateProvider : IRateProvider
    {
        public RateSourceInfo RateSourceInfo => new("bitcoinjungle", "BitcoinJungle", "https://api.bullbitcoin.com/price");
        private readonly HttpClient _httpClient;
        public BitcoinJungleRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://api.bullbitcoin.com/price", cancellationToken);
            response.EnsureSuccessStatusCode();
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            var list = new List<PairRate>();
            var value = jobj["result"]["fromToPrice"].Value<decimal>();
            // var usdValue = jobj["BTCUSD"].Value<decimal>();

            list.Add(new PairRate(new CurrencyPair("BTC", "CRC"), new BidAsk(value)));
            // list.Add(new PairRate(new CurrencyPair("BTC", "USD"), new BidAsk(usdValue)));

            return list.ToArray();
        }
    }
}
