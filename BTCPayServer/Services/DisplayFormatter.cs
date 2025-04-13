using System;
using System.Globalization;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
using BTCPayServer.Services.Reporting;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services;

public class DisplayFormatter
{
    private readonly CurrencyNameTable _currencyNameTable;

    public DisplayFormatter(CurrencyNameTable currencyNameTable)
    {
        _currencyNameTable = currencyNameTable;
    }

    public enum CurrencyFormat
    {
        Code,
        Symbol,
        CodeAndSymbol,
        None
    }

    /// <summary>
    /// Format a currency, rounded to significant divisibility
    /// </summary>
    /// <param name="value">The value</param>
    /// <param name="currency">Currency code</param>
    /// <param name="format">The format, defaults to amount + code, e.g. 1.234,56 USD</param>
    /// <returns>Formatted amount and currency string</returns>
    public string Currency(decimal value, string currency, CurrencyFormat format = CurrencyFormat.Code, int? divisibility = null)
    {
        var provider = _currencyNameTable.GetNumberFormatInfo(currency, true);
        var currencyData = _currencyNameTable.GetCurrencyData(currency, true);
        var div = divisibility is int d ? d :  currencyData.Divisibility;
        value = value.RoundToSignificant(ref div);
        if (divisibility != provider.CurrencyDecimalDigits)
        {
            provider = (NumberFormatInfo)provider.Clone();
            provider.CurrencyDecimalDigits = div;
        }
        var formatted = value.ToString("C", provider);

        // Ensure we are not using the symbol for BTC — we made that design choice consciously.
        if (format == CurrencyFormat.Symbol && currencyData.Code == "BTC")
        {
            format = CurrencyFormat.Code;
        }

        return format switch
        {
            CurrencyFormat.None => formatted.Replace(provider.CurrencySymbol, "").Trim(),
            CurrencyFormat.Code => $"{formatted.Replace(provider.CurrencySymbol, "").Trim()} {currency}",
            CurrencyFormat.Symbol => formatted,
            CurrencyFormat.CodeAndSymbol => $"{formatted} ({currency})",
            _ => throw new ArgumentOutOfRangeException(nameof(format), format, null)
        };
    }

    public string Currency(string value, string currency, CurrencyFormat format = CurrencyFormat.Code)
    {
        return Currency(decimal.Parse(value, CultureInfo.InvariantCulture), currency, format);
    }

    public JObject ToFormattedAmount(decimal value, string currency)
    {
        var currencyData = _currencyNameTable.GetCurrencyData(currency, true);
        var divisibility = currencyData.Divisibility;
        return new FormattedAmount(value, divisibility).ToJObject();
    }
}
