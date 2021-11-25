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
            var response = await _httpClient.GetAsync("https://public.bitbank.cc/tickers", cancellationToken);
            var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
            // bitbank API failure
            /*
            if (jobj["success"] as int != 1)
            {
                var errorCode = (jobj["data"] as JObject)["code"] as int;
                // TODO: do something with the error code.
                // human readable error code list here:
                // https://github.com/bitbankinc/bitbank-api-docs/blob/master/errors.md
            }
            */

            // CHANGED: data is now an array of objects, each containing a key "pair"
            // which contains what used to be in the key of the object
            return ((jobj["data"] as JArray) ?? new JArray())
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
