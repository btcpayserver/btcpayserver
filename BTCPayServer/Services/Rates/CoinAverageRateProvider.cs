using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;

namespace BTCPayServer.Services.Rates
{
    public class CoinAverageException : Exception
    {
        public CoinAverageException(string message) : base(message)
        {

        }
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
            if (rates.TryGetValue(currency, out decimal result))
                return result;
            throw new RateUnavailableException(currency);
        }

        private async Task<Dictionary<string, decimal>> GetRatesCore()
        {
            HttpResponseMessage resp = null;
            if (Exchange == null)
            {
                resp = await _Client.GetAsync($"https://apiv2.bitcoinaverage.com/indices/{Market}/ticker/short");
            }
            else
            {
                resp = await _Client.GetAsync($"https://apiv2.bitcoinaverage.com/exchanges/{Exchange}");
            }
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
                              .Where(p => p.Name.StartsWith(CryptoCode, StringComparison.OrdinalIgnoreCase))
                              .ToDictionary(p => p.Name.Substring(CryptoCode.Length, p.Name.Length - CryptoCode.Length), p =>
                              {
                                  if (Exchange == null)
                                  {
                                      return ToDecimal(p.Value["last"]);
                                  }
                                  else
                                  {
                                      return ToDecimal(p.Value["bid"]);
                                  }
                              });
            }
        }

        private decimal ToDecimal(JToken token)
        {
            return decimal.Parse(token.Value<string>(), System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowDecimalPoint);
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
    }
}
