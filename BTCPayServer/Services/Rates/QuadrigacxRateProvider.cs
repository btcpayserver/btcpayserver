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
    public class QuadrigacxRateProvider : IRateProvider, IHasExchangeName
    {
        public const string QuadrigacxName = "quadrigacx";
        static HttpClient _Client = new HttpClient();

        public string ExchangeName => QuadrigacxName;

        private bool TryToBidAsk(JObject p, out BidAsk v)
        {
            v = null;
            JToken bid = p.Property("bid")?.Value;
            JToken ask = p.Property("ask")?.Value;
            if (bid == null || ask == null)
                return false;
            if (!decimal.TryParse(bid.Value<string>(), System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v1) ||
               !decimal.TryParse(bid.Value<string>(), System.Globalization.NumberStyles.AllowExponent | System.Globalization.NumberStyles.AllowDecimalPoint, CultureInfo.InvariantCulture, out var v2) ||
               v1 <= 0m || v2 <= 0m || v1 > v2)
                return false;
            v = new BidAsk(v1, v2);
            return true;
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
                if (!TryToBidAsk((JObject)prop.Value, out var v))
                    continue;
                rate.BidAsk = v;
                exchangeRates.Add(rate);
            }
            return exchangeRates;
        }
    }
}
