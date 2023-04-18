using System;
using System.Globalization;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;

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
        CodeAndSymbol
    }

    /// <summary>
    /// Format a currency, rounded to significant divisibility
    /// </summary>
    /// <param name="value">The value</param>
    /// <param name="currency">Currency code</param>
    /// <param name="format">The format, defaults to amount + code, e.g. 1.234,56 USD</param>
    /// <returns>Formatted amount and currency string</returns>
    public string Currency(decimal value, string currency, CurrencyFormat format = CurrencyFormat.Code)
    {
        var provider = _currencyNameTable.GetNumberFormatInfo(currency, true);
        var currencyData = _currencyNameTable.GetCurrencyData(currency, true);
        var divisibility = currencyData.Divisibility;
        value = value.RoundToSignificant(ref divisibility);
        if (divisibility != provider.CurrencyDecimalDigits)
        {
            provider = (NumberFormatInfo)provider.Clone();
            provider.CurrencyDecimalDigits = divisibility;
        }
        var formatted = value.ToString("C", provider);

        return format switch
        {
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
}
