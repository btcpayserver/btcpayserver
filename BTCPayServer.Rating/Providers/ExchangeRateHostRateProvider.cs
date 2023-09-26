using System;
using System.Collections.Generic;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates;


public class ExchangeRateHostRateProvider : IRateProvider
{
    public RateSourceInfo RateSourceInfo => new("exchangeratehost", "Exchangerate.host", "https://api.exchangerate.host/latest?base=BTC");
    private readonly HttpClient _httpClient;
    public ExchangeRateHostRateProvider(HttpClient httpClient)
    {
        _httpClient = httpClient ?? new HttpClient();
    }

    public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
    {
        var response = await _httpClient.GetAsync(RateSourceInfo.Url, cancellationToken);
        response.EnsureSuccessStatusCode();
        var jobj = await response.Content.ReadAsAsync<JObject>(cancellationToken);
        if(jobj["success"].Value<bool>() is not true || !jobj["base"].Value<string>().Equals("BTC", StringComparison.InvariantCulture))
            throw new Exception("exchangerate.host returned a non success response or the base currency was not the requested one (BTC)");
        var results = (JObject) jobj["rates"] ;
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
