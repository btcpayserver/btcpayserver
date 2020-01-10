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
    public class CoinGeckoRateProvider : IRateProvider, IHasExchangeName
    {
        private readonly HttpClient Client;
        public static string CoinGeckoName { get; } = "coingecko";
        public string Exchange { get; set; }
        public string ExchangeName => Exchange ?? CoinGeckoName;

        public CoinGeckoRateProvider(IHttpClientFactory httpClientFactory)
        {
            if (httpClientFactory == null)
            {
                return;;
            }
            Client = httpClientFactory.CreateClient();
            Client.BaseAddress = new Uri("https://api.coingecko.com/api/v3/");
            Client.DefaultRequestHeaders.Add("Accept", "application/json");
        }

        private IEnumerable<AvailableRateProvider> _availableExchanges;

        public virtual async Task<IEnumerable<AvailableRateProvider>> GetAvailableExchanges(bool reload = false)
        {
            if (_availableExchanges != null && !reload) return _availableExchanges;
            var resp = await Client.GetAsync("exchanges/list");
            resp.EnsureSuccessStatusCode();
            _availableExchanges = JArray.Parse(await resp.Content.ReadAsStringAsync())
                .Select(token =>
                    new AvailableRateProvider(token["id"].ToString().ToLowerInvariant(), token["name"].ToString(),
                        $"{Client.BaseAddress}exchanges/{token["id"]}/tickers"));

            return _availableExchanges;
        }

        public virtual Task<ExchangeRates> GetRatesAsync(CancellationToken cancellationToken)
        {
            return ExchangeName == CoinGeckoName ? GetCoinGeckoRates() : GetCoinGeckoExchangeSpecificRates();
        }

        private async Task<ExchangeRates> GetCoinGeckoRates()
        {
            var resp = await Client.GetAsync("exchange_rates");
            resp.EnsureSuccessStatusCode();
            return new ExchangeRates(JObject.Parse(await resp.Content.ReadAsStringAsync()).GetValue("rates").Children()
                .Select(token => new ExchangeRate(CoinGeckoName,
                    new CurrencyPair("BTC", ((JProperty)token).Name.ToString()),
                    new BidAsk(((JProperty)token).Value["value"].Value<decimal>()))));
        }

        private async Task<ExchangeRates> GetCoinGeckoExchangeSpecificRates(int page = 1)
        {
            var resp = await Client.GetAsync($"exchanges/{Exchange}/tickers?page={page}");

            resp.EnsureSuccessStatusCode();
            List<ExchangeRate> result = JObject.Parse(await resp.Content.ReadAsStringAsync()).GetValue("tickers")
                .Select(token => new ExchangeRate(ExchangeName,
                    new CurrencyPair(token.Value<string>("base"), token.Value<string>("target")),
                    new BidAsk(token.Value<decimal>("last")))).ToList();
            if (page == 1 && resp.Headers.TryGetValues("total", out var total) &&
                resp.Headers.TryGetValues("per-page", out var perPage))
            {
                var totalItems = int.Parse(total.First());
                var perPageItems = int.Parse(perPage.First());

                var totalPages = totalItems / perPageItems;
                if (totalItems % perPageItems != 0)
                {
                    totalPages++;
                }

                var tasks = new List<Task<ExchangeRates>>();
                for (int i = 2; i <= totalPages; i++)
                {
                    tasks.Add(GetCoinGeckoExchangeSpecificRates(i));
                }

                foreach (var t in (await Task.WhenAll(tasks)))
                {
                    result.AddRange(t);
                }
            }

            return new ExchangeRates(result);
        }
    }
}
