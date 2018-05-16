using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Net.Http;
using System.Threading.Tasks;
using BTCPayServer.Rating;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Rates
{
    public class QuadrigacxRateProvider : IRateProvider
    {
        public const string QuadrigacxName = "quadrigacx";
        static HttpClient _Client = new HttpClient();

        private bool TryToDecimal(JObject p, out decimal v)
        {
            v = 0.0m;
            JToken token = p.Property("bid")?.Value;
            if (token == null)
                return false;
            return decimal.TryParse(token.Value<string>(), System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out v);
        }

        public async Task<ExchangeRates> GetRatesAsync()
        {
            var response = await _Client.GetAsync($"https://api.quadrigacx.com/v2/ticker?book=all");
            response.EnsureSuccessStatusCode();
            var rates = JObject.Parse(await response.Content.ReadAsStringAsync());

            var exchangeRates = new ExchangeRates();
            foreach (var prop in rates.Properties())
            {
                var rate = new ExchangeRate();
                if (!Rating.CurrencyPair.TryParse(prop.Name, out var pair))
                    continue;
                rate.CurrencyPair = pair;
                rate.Exchange = QuadrigacxName;
                if (!TryToDecimal((JObject)prop.Value, out var v))
                    continue;
                rate.Value = v;
                exchangeRates.Add(rate);
            }
            return exchangeRates;
        }
    }
}
