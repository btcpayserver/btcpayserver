using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class ZanoRateProvider : IRateProvider
    {
        public const string ZanoProviderName = "zano";
        
        private readonly HttpClient _httpClient;
        private readonly ILogger<ZanoRateProvider> _logger;

        public RateSourceInfo RateSourceInfo => new(ZanoProviderName, "Zano Direct", "https://api.coingecko.com/api/v3/simple/price?ids=zano&vs_currencies=usd,btc,eur");

        public ZanoRateProvider(IHttpClientFactory httpClientFactory, ILogger<ZanoRateProvider> logger)
        {
            _httpClient = httpClientFactory.CreateClient();
            _httpClient.BaseAddress = new Uri("https://api.coingecko.com/api/v3/");
            _httpClient.DefaultRequestHeaders.Add("Accept", "application/json");
            _logger = logger;
        }

        public async Task<PairRate[]> GetRatesAsync(CancellationToken cancellationToken)
        {
            var rates = new List<PairRate>();

            try
            {
                // Try CoinGecko first (most reliable for Zano)
                var coinGeckoRates = await GetCoinGeckoRatesAsync(cancellationToken);
                if (coinGeckoRates.Any())
                {
                    rates.AddRange(coinGeckoRates);
                    _logger.LogInformation("Successfully fetched Zano rates from CoinGecko");
                }

                // Try Yadio as backup
                if (!rates.Any())
                {
                    var yadioRates = await GetYadioRatesAsync(cancellationToken);
                    if (yadioRates.Any())
                    {
                        rates.AddRange(yadioRates);
                        _logger.LogInformation("Successfully fetched Zano rates from Yadio");
                    }
                }

                // Try additional sources if needed
                if (!rates.Any())
                {
                    var additionalRates = await GetAdditionalRatesAsync(cancellationToken);
                    if (additionalRates.Any())
                    {
                        rates.AddRange(additionalRates);
                        _logger.LogInformation("Successfully fetched Zano rates from additional sources");
                    }
                }
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error fetching Zano rates from all sources");
            }

            return rates.ToArray();
        }

        private async Task<PairRate[]> GetCoinGeckoRatesAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var response = await GetWithBackoffAsync("simple/price?ids=zano&vs_currencies=usd,btc,eur,gbp,jpy,cad,aud,chf", cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var jobj = JObject.Parse(json);
                
                var zanoData = jobj["zano"] as JObject;
                if (zanoData == null)
                    return Array.Empty<PairRate>();

                var rates = new List<PairRate>();

                // Add all available currency pairs
                foreach (var property in zanoData.Properties())
                {
                    if (decimal.TryParse(property.Value.ToString(), out var rate) && rate > 0)
                    {
                        rates.Add(new PairRate(new CurrencyPair("ZANO", property.Name.ToUpperInvariant()), new BidAsk(rate)));
                    }
                }

                return rates.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch rates from CoinGecko");
                return Array.Empty<PairRate>();
            }
        }

        private async Task<PairRate[]> GetYadioRatesAsync(CancellationToken cancellationToken)
        {
            try
            {
                using var response = await _httpClient.GetAsync("https://api.yadio.io/exrates/ZANO", cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var jobj = JObject.Parse(json);
                
                var zanoData = jobj["ZANO"] as JObject;
                if (zanoData == null)
                    return Array.Empty<PairRate>();

                var rates = new List<PairRate>();

                foreach (var property in zanoData.Properties())
                {
                    if (decimal.TryParse(property.Value.ToString(), out var rate) && rate > 0)
                    {
                        rates.Add(new PairRate(new CurrencyPair("ZANO", property.Name), new BidAsk(rate)));
                    }
                }

                return rates.ToArray();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch rates from Yadio");
                return Array.Empty<PairRate>();
            }
        }

        private async Task<PairRate[]> GetAdditionalRatesAsync(CancellationToken cancellationToken)
        {
            try
            {
                // Try alternative CoinGecko endpoint for more comprehensive data
                using var response = await GetWithBackoffAsync("coins/zano/market_chart?vs_currency=usd&days=1&interval=daily", cancellationToken);
                response.EnsureSuccessStatusCode();
                
                var json = await response.Content.ReadAsStringAsync(cancellationToken);
                var jobj = JObject.Parse(json);
                
                var prices = jobj["prices"] as JArray;
                if (prices == null || !prices.Any())
                    return Array.Empty<PairRate>();

                // Get the latest price (last element in the array)
                var latestPrice = prices.Last();
                if (latestPrice is JArray priceArray && priceArray.Count >= 2)
                {
                    var usdRate = priceArray[1].Value<decimal>();
                    return new[] { new PairRate(new CurrencyPair("ZANO", "USD"), new BidAsk(usdRate)) };
                }

                return Array.Empty<PairRate>();
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Failed to fetch rates from additional sources");
                return Array.Empty<PairRate>();
            }
        }

        private async Task<HttpResponseMessage> GetWithBackoffAsync(string request, CancellationToken cancellationToken)
        {
            TimeSpan retryWait = TimeSpan.FromSeconds(1);
retry:
            var resp = await _httpClient.GetAsync(request, cancellationToken);
            if (resp.StatusCode == System.Net.HttpStatusCode.TooManyRequests)
            {
                resp.Dispose();
                if (retryWait < TimeSpan.FromSeconds(60))
                {
                    await Task.Delay(retryWait, cancellationToken);
                    retryWait = TimeSpan.FromSeconds(retryWait.TotalSeconds * 2);
                    goto retry;
                }
                resp.EnsureSuccessStatusCode();
            }
            return resp;
        }
    }
} 