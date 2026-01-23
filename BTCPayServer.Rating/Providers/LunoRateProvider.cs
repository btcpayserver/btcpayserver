using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net.Http;
using System.Text.Json.Serialization;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Services.Rates;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Rating.Providers;

public class LunoRateProvider(HttpClient httpClient) : IRateProvider
{
    class GetTickerResponse
    {
        public class Ticker
        {
            public string Pair { get; set; }
            public string Bid { get; set; }
            public string Ask { get; set; }
        }

        public Ticker[] Tickers { get; set; }
    }
    public RateSourceInfo RateSourceInfo  => new("luno", "Luno", "https://api.luno.com/api/1/tickers");
    public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
    {
        using var response = await httpClient.GetAsync("https://api.luno.com/api/1/tickers", cancellationToken);
        var resp = await response.Content.ReadAsAsync<GetTickerResponse>(cancellationToken);
        List<PairRate> rates = new();
        foreach (var ticker in resp.Tickers)
        {
            if (!CurrencyPair.TryParse(Normalize(ticker.Pair), out var pair) ||
                !decimal.TryParse(ticker.Bid, NumberStyles.Any, CultureInfo.InvariantCulture, out var bid) ||
                !decimal.TryParse(ticker.Ask, NumberStyles.Any, CultureInfo.InvariantCulture, out var ask))
                continue;
            if (bid > ask)
                continue;
            rates.Add(new PairRate(pair, new BidAsk(bid, ask)));
        }
        return rates.ToArray();
    }

    private string Normalize(string tickerPair) => tickerPair.Replace("XBT", "BTC", StringComparison.OrdinalIgnoreCase);
}
