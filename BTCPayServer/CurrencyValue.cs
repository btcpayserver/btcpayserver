using System;
using System.Globalization;
using System.Text.RegularExpressions;
using BTCPayServer.Services.Rates;

namespace BTCPayServer
{
    public class CurrencyValue
    {
        static readonly Regex _Regex = new Regex("^([0-9]+(\\.[0-9]+)?)\\s*([a-zA-Z]+)$");
        public static bool TryParse(string str, out CurrencyValue value)
        {
            value = null;
            var match = _Regex.Match(str);
            if (!match.Success ||
                !decimal.TryParse(match.Groups[1].Value, out var v))
                return false;

            var currency = match.Groups[match.Groups.Count - 1].Value.ToUpperInvariant();
            var currencyData = CurrencyNameTable.Instance.GetCurrencyData(currency, false);
            if (currencyData == null)
                return false;
            v = Math.Round(v, currencyData.Divisibility);
            value = new CurrencyValue()
            {
                Value = v,
                Currency = currency
            };
            return true;
        }

        public decimal Value { get; set; }
        public string Currency { get; set; }

        public override string ToString()
        {
            return Value.ToString(CultureInfo.InvariantCulture) + " " + Currency;
        }
    }
}
