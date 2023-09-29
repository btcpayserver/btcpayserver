using System;
using System.Globalization;
using BTCPayServer.Rating;
using BTCPayServer.Services.Rates;
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

    public DisplayFormatterInfo Info(decimal value, string currency)
    {
        var formatProvider = _currencyNameTable.GetNumberFormatInfo(currency, true);
        var stringProvider = (NumberFormatInfo)formatProvider.Clone();
        stringProvider.CurrencySymbol = "";
        stringProvider.NumberGroupSeparator = "";
        stringProvider.NumberDecimalSeparator = ".";
        var currencyData = _currencyNameTable.GetCurrencyData(currency, true);
        var divisibility = currencyData.Divisibility;
        return new DisplayFormatterInfo
        {
            Amount = value,
            AmountString = value.ToString("C", stringProvider).Trim(),
            AmountFormatted = value.ToString("C", formatProvider),
            Divisibility = divisibility,
            Currency = currency,
            Symbol = formatProvider.CurrencySymbol
        };
    }
}

public class DisplayFormatterInfo
{
    public decimal Amount { get; set; }
    public string AmountString { get; set; }
    public string AmountFormatted { get; set; }
    public int Divisibility { get; set; }
    public string Currency { get; set; }
    public string Symbol { get; set; }

    public JObject ToJObject()
    {
        return new JObject
        {
            {"amount", Amount},
            {"amountString", AmountString},
            {"amountFormatted", AmountFormatted},
            {"divisibility", Divisibility},
            {"currency", Currency},
            {"symbol", Symbol}
        };
    }
}
