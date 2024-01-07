using System;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class BitbankRateProvider : IRateProvider
    {
        private readonly HttpClient _httpClient;

        public RateSourceInfo RateSourceInfo => new("bitbank", "Bitbank", "https://public.bitbank.cc/tickers");

        public BitbankRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            using var response = await _httpClient.GetAsync("https://public.bitbank.cc/tickers", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            var data = jobj.ContainsKey("data") ? jobj["data"] : null;
            if (jobj["success"]?.Value<int>() != 1)
            {
                var errorCode = data is null ? "Unknown" : data["code"].Value<string>();
                throw new Exception(
                    $"BitBank Rates API Error: {errorCode}. See https://github.com/bitbankinc/bitbank-api-docs/blob/master/errors.md for more details.");
            }
            return ((data as JArray) ?? new JArray())
                .Where(p => p["buy"].Type != JTokenType.Null && p["sell"].Type != JTokenType.Null)
                .Select(item => new PairRate(CurrencyPair.Parse(item["pair"].ToString()), CreateBidAsk(item as JObject)))
                .ToArray();
        }

        private static BidAsk CreateBidAsk(JObject o)
        {
            var buy = o["buy"].Value<decimal>();
            var sell = o["sell"].Value<decimal>();
            // Bug from their API (https://github.com/btcpayserver/btcpayserver/issues/741)
            return buy < sell ? new BidAsk(buy, sell) : new BidAsk(sell, buy);
        }
    }
}
