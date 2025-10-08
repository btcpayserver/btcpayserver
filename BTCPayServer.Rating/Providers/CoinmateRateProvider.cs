using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Rating.Providers;

public class CoinmateRateProvider : IRateProvider
{
    private readonly HttpClient _httpClient;

    public CoinmateRateProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public RateSourceInfo RateSourceInfo =>  new("coinmate", "Coinmate", "https://coinmate.io/api/tickerAll");
    public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync("https://coinmate.io/api/tickerAll", cancellationToken);
        response.EnsureSuccessStatusCode();
        var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);

        var data = jobj["data"];
        if (data == null)
        {
            return [];
        }

        var list = new List<PairRate>();

        foreach (var pairProperty in data.Children<JProperty>())
        {
            var pairName = pairProperty.Name;
            var pairParts = pairName.Split('_');
            if (pairParts.Length != 2)
                continue;

            var baseCurrency = pairParts[0];
            var quoteCurrency = pairParts[1];

            var details = pairProperty.Value;
            var bid = details.Value<decimal>("bid");
            var ask = details.Value<decimal>("ask");

            list.Add(new PairRate(new CurrencyPair(baseCurrency, quoteCurrency), new BidAsk(bid, ask)));
        }

        return list.ToArray();
    }
}
