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
            List<Rate> result = new List<Rate>();
            // https://www.quadrigacx.com/api_info
            foreach(var q in new[] { (Crypto: "BTC", Currency:"CAD" ),
                (Crypto: "BTC", Currency:"USD" ),
                (Crypto: "ETH", Currency:"CAD" ),
                (Crypto: "LTC", Currency:"CAD" ),
                (Crypto: "BCH", Currency:"CAD" ),
                (Crypto: "BTG", Currency:"CAD" ) }
            .Where(c => CryptoCode == c.Crypto)
            .Select(c => (Crypto: c.Crypto, Currency: c.Currency, Rate: GetRatesAsyncCore(c.Crypto, c.Currency))))
            {
                try
                {
                    result.Add(new Rate() { Currency = q.Currency, Value = await q.Rate });
                }
                catch(RateUnavailableException) { }
            }
            return result;
        }
    }
}
