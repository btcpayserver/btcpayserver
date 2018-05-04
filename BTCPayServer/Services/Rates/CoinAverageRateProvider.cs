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
using BTCPayServer.Rating;

namespace BTCPayServer.Services.Rates
{
    public class CoinAverageException : Exception
    {
        public CoinAverageException(string message) : base(message)
        {

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
        public const string CoinAverageName = "coinaverage";
        public CoinAverageRateProvider()
        {
            
        }
        static HttpClient _Client = new HttpClient();

        public string Exchange { get; set; } = CoinAverageName;

        public string CryptoCode { get; set; }

        public string Market
        {
            get; set;
        } = "global";

        public ICoinAverageAuthenticator Authenticator { get; set; }

        private bool TryToDecimal(JProperty p, out decimal v)
        {
            JToken token = p.Value[Exchange == CoinAverageName ? "last" : "bid"];
            return decimal.TryParse(token.Value<string>(), System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v);
        }

        public async Task<ExchangeRates> GetRatesAsync()
        {
            string url = Exchange == CoinAverageName ? $"https://apiv2.bitcoinaverage.com/indices/{Market}/ticker/short"
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
                if (Exchange != CoinAverageName)
                {
                    rates = (JObject)rates["symbols"];
                }

                var exchangeRates = new ExchangeRates();
                foreach (var prop in rates.Properties())
                {
                    ExchangeRate exchangeRate = new ExchangeRate();
                    exchangeRate.Exchange = Exchange;
                    if (!TryToDecimal(prop, out decimal value))
                        continue;
                    exchangeRate.Value = value;
                    if(CurrencyPair.TryParse(prop.Name, out var pair))
                    {
                        exchangeRate.CurrencyPair = pair;
                        exchangeRates.Add(exchangeRate);
                    }
                }
                return exchangeRates;
            }
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
            var totalPeriod = jobj["total_period"].Value<string>();
            if (totalPeriod == "24h")
            {
                response.TotalPeriod = TimeSpan.FromHours(24);
            }
            else if (totalPeriod == "30d")
            {
                response.TotalPeriod = TimeSpan.FromDays(30);
            }
            else
            {
                response.TotalPeriod = TimeSpan.FromSeconds(jobj["total_period"].Value<int>());
            }
            response.RequestsLeft = jobj["requests_left"].Value<int>();
            response.RequestsPerPeriod = jobj["requests_per_period"].Value<int>();
            return response;
        }

        public async Task<GetExchangeTickersResponse> GetExchangeTickersAsync()
        {
            var request = new HttpRequestMessage(HttpMethod.Get, "https://apiv2.bitcoinaverage.com/symbols/exchanges/ticker");
            var auth = Authenticator;
            if (auth != null)
            {
                await auth.AddHeader(request);
            }
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
        public int RequestsPerPeriod { get; set; }
        public TimeSpan TotalPeriod { get; set; }
    }
}
