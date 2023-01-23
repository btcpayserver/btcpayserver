using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates;

public class BudaRateProvider : IRateProvider
{
    private readonly HttpClient _httpClient;
    public BudaRateProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public RateSourceInfo RateSourceInfo => new RateSourceInfo("buda", "Buda", "https://www.buda.com/api/v2/markets/btc-clp/ticker");

    public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("https://www.buda.com/api/v2/markets/btc-clp/ticker", cancellationToken);
        var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
        var minAsk = jobj["ticker"]["min_ask"][0].Value<decimal>();
        var maxBid = jobj["ticker"]["max_bid"][0].Value<decimal>();
        return new[] { new PairRate(new CurrencyPair("BTC", "CLP"), new BidAsk(maxBid, minAsk)) };
    }
}
