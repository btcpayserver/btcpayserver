using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates;

public class FreeCurrencyRatesRateProvider : IRateProvider
{
    public RateSourceInfo RateSourceInfo => new("free-currency-rates", "Free Currency Rates", "https://currency-api.pages.dev/v1/currencies/btc.min.json");
    private readonly HttpClient _httpClient;
    public FreeCurrencyRatesRateProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
    {
        using var response = await _httpClient.GetAsync(RateSourceInfo.Url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
        var results = (JObject) jobj["btc"] ;
        //key value is currency code to rate value
        var list = new List<PairRate>();
        foreach (var item in results)
        {
            string name = item.Key;
            var value = item.Value.Value<decimal>();
            list.Add(new PairRate(new CurrencyPair("BTC", name), new BidAsk(value)));
        }

        return list.ToArray();
    }
}
