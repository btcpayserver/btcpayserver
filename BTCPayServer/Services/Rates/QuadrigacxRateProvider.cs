using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class QuadrigacxRateProvider : IRateProvider
    {
        public QuadrigacxRateProvider(string crypto)
        {
            CryptoCode = crypto;
        }
        public string CryptoCode { get; set; }
        static HttpClient _Client = new HttpClient();
        public async Task<decimal> GetRateAsync(string currency)
        {
            return await GetRatesAsyncCore(CryptoCode, currency);
        }

        private async Task<decimal> GetRatesAsyncCore(string cryptoCode, string currency)
        {
            var response = await _Client.GetAsync($"https://api.quadrigacx.com/v2/ticker?book={cryptoCode.ToLowerInvariant()}_{currency.ToLowerInvariant()}");
            response.EnsureSuccessStatusCode();
            var rates = JObject.Parse(await response.Content.ReadAsStringAsync());
            if (!TryToDecimal(rates, out var result))
                throw new RateUnavailableException(currency);
            return result;
        }

        private bool TryToDecimal(JObject p, out decimal v)
        {
            v = 0.0m;
            JToken token = p.Property("bid")?.Value;
            if (token == null)
                return false;
            return decimal.TryParse(token.Value<string>(), System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v);
        }

        public async Task<ICollection<Rate>> GetRatesAsync()
        {
            var response = await _Client.GetAsync($"https://api.quadrigacx.com/v2/ticker?book=all");
            response.EnsureSuccessStatusCode();
            var rates = JObject.Parse(await response.Content.ReadAsStringAsync());

            List<Rate> result = new List<Rate>();
            foreach (var prop in rates.Properties())
            {
                var rate = new Rate();
                var splitted = prop.Name.Split('_');
                var crypto = splitted[0].ToUpperInvariant();
                if (crypto != CryptoCode)
                    continue;
                rate.Currency = splitted[1].ToUpperInvariant();
                TryToDecimal((JObject)prop.Value, out var v);
                rate.Value = v;
                result.Add(rate);
            }
            return result;
        }
    }
}
