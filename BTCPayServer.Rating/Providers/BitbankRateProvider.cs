using System;
using System.Collections.Generic;
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
        public BitbankRateProvider(HttpClient httpClient)
        {
            _httpClient = httpClient ?? new HttpClient();
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var response = await _httpClient.GetAsync("https://public.bitbank.cc/prices", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            return ((jobj["data"] as JObject) ?? new JObject())
                .Properties()
                .Select(p => new PairRate(CurrencyPair.Parse(p.Name), CreateBidAsk(p)))
                .ToArray();
        }

        private static BidAsk CreateBidAsk(JProperty p)
        {
            var buy = p.Value["buy"].Value<decimal>();
            var sell = p.Value["sell"].Value<decimal>();
            // Bug from their API (https://github.com/btcpayserver/btcpayserver/issues/741)
            return buy < sell ? new BidAsk(buy, sell) : new BidAsk(sell, buy);
        }
    }
}
