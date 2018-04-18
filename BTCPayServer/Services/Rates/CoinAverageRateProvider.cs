using Newtonsoft.Json;
using Microsoft.Extensions.DependencyInjection;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;
using System.ComponentModel;

namespace BTCPayServer.Services.Rates
{
    public class CoinAverageException : Exception
    {
        public CoinAverageException(string message) : base(message)
        {

        }
    }

    public class CoinAverageRateProviderDescription : RateProviderDescription
    {
        public CoinAverageRateProviderDescription(string crypto)
        {
            CryptoCode = crypto;
        }

        public string CryptoCode { get; set; }

        public CoinAverageRateProvider CreateRateProvider(IServiceProvider serviceProvider)
        {
            return new CoinAverageRateProvider(CryptoCode)
            {
                Authenticator = serviceProvider.GetService<ICoinAverageAuthenticator>()
            };
        }

        IRateProvider RateProviderDescription.CreateRateProvider(IServiceProvider serviceProvider)
        {
            return CreateRateProvider(serviceProvider);
        }
    }

    public class GetExchangeTickersResponse
    {
        public class Exchange
        {
            public string Name { get; set; }
            [JsonProperty("display_name")]
            public string DisplayName { get; set; }
            public string[] Symbols { get; set; }
        }
        public bool Success { get; set; }
        public Exchange[] Exchanges { get; set; }
    }

    public class RatesSetting
    {
        public string PublicKey { get; set; }
        public string PrivateKey { get; set; }
        [DefaultValue(15)]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
        public int CacheInMinutes { get; set; } = 15;
    }

    public interface ICoinAverageAuthenticator
    {
        Task AddHeader(HttpRequestMessage message);
    }    

    public class CoinAverageRateProvider : IRateProvider
    {
        static HttpClient _Client = new HttpClient();

        public CoinAverageRateProvider(string cryptoCode)
        {
            CryptoCode = cryptoCode ?? "BTC";
        }

        public string Exchange { get; set; }

        public string CryptoCode { get; set; }

        public string Market
        {
            get; set;
        } = "global";
        public async Task<decimal> GetRateAsync(string currency)
        {
            var rates = await GetRatesCore();
            return GetRate(rates, currency);
        }

        private decimal GetRate(Dictionary<string, decimal> rates, string currency)
        {
            if (currency == "BTC")
                return 1.0m;
            if (rates.TryGetValue(currency, out decimal result))
                return result;
            throw new RateUnavailableException(currency);
        }

        public ICoinAverageAuthenticator Authenticator { get; set; }

        private async Task<Dictionary<string, decimal>> GetRatesCore()
        {
            string url = Exchange == null ? $"https://apiv2.bitcoinaverage.com/indices/{Market}/ticker/short"
                                          : $"https://apiv2.bitcoinaverage.com/exchanges/{Exchange}";

            var request = new HttpRequestMessage(HttpMethod.Get, url);
            var auth = Authenticator;
            if (auth != null)
            {
                await auth.AddHeader(request);
            }
            var resp = await _Client.SendAsync(request);
            using (resp)
            {

                if ((int)resp.StatusCode == 401)
                    throw new CoinAverageException("Unauthorized access to the API");
                if ((int)resp.StatusCode == 429)
                    throw new CoinAverageException("Exceed API limits");
                if ((int)resp.StatusCode == 403)
                    throw new CoinAverageException("Unauthorized access to the API, premium plan needed");
                resp.EnsureSuccessStatusCode();
                var rates = JObject.Parse(await resp.Content.ReadAsStringAsync());
                if(Exchange != null)
                {
                    rates = (JObject)rates["symbols"];
                }
                return rates.Properties()
                              .Where(p => p.Name.StartsWith(CryptoCode, StringComparison.OrdinalIgnoreCase) && TryToDecimal(p, out decimal unused))
                              .ToDictionary(p => p.Name.Substring(CryptoCode.Length, p.Name.Length - CryptoCode.Length), p =>
                              {
                                  TryToDecimal(p, out decimal v);
                                  return v;
                              });
            }
        }

        private bool TryToDecimal(JProperty p, out decimal v)
        {
            JToken token = p.Value[Exchange == null ? "last" : "bid"];
            return decimal.TryParse(token.Value<string>(), System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v);
        }

        public async Task<ICollection<Rate>> GetRatesAsync()
        {
            var rates = await GetRatesCore();
            return rates.Select(o => new Rate()
            {
                Currency = o.Key,
                Value = o.Value
            }).ToList();
        }

        public async Task TestAuthAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://apiv2.bitcoinaverage.com/blockchain/tx_price/BTCUSD/8a3b4394ba811a9e2b0bbf3cc56888d053ea21909299b2703cdc35e156c860ff");
            var auth = Authenticator;
            if (auth != null)
            {
                await auth.AddHeader(request);
            }
            var resp = await _Client.SendAsync(request);
            resp.EnsureSuccessStatusCode();
        }

        public async Task<GetRateLimitsResponse> GetRateLimitsAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://apiv2.bitcoinaverage.com/info/ratelimits");
            var auth = Authenticator;
            if (auth != null)
            {
                await auth.AddHeader(request);
            }
            var resp = await _Client.SendAsync(request);
            resp.EnsureSuccessStatusCode();
            var jobj = JObject.Parse(await resp.Content.ReadAsStringAsync());
            var response = new GetRateLimitsResponse();
            response.CounterReset = TimeSpan.FromSeconds(jobj["counter_reset"].Value<int>());
            response.RequestsLeft = jobj["requests_left"].Value<int>();
            return response;
        }

        public async Task<GetExchangeTickersResponse> GetExchangeTickersAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://apiv2.bitcoinaverage.com/symbols/exchanges/ticker");
            var resp = await _Client.SendAsync(request);
            resp.EnsureSuccessStatusCode();
            var jobj = JObject.Parse(await resp.Content.ReadAsStringAsync());
            var response = new GetExchangeTickersResponse();
            response.Success = jobj["success"].Value<bool>();
            var exchanges = (JObject)jobj["exchanges"];
            response.Exchanges = exchanges
                .Properties()
                .Select(p => 
                {
                    var exchange = JsonConvert.DeserializeObject<GetExchangeTickersResponse.Exchange>(p.Value.ToString());
                    exchange.Name = p.Name;
                    return exchange;
                })
                .ToArray();
            return response;
        }
    }

    public class GetRateLimitsResponse
    {
        public TimeSpan CounterReset { get; set; }
        public int RequestsLeft { get; set; }
    }
}
