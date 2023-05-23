using System;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Net.Http.Headers;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class WithdrawRequestData
{
    public string PaymentMethod { set; get; }
    [JsonConverter(typeof(JsonConverters.TradeQuantityJsonConverter))]
    public TradeQuantity Qty { set; get; }

    public WithdrawRequestData()
    {

    }

    public WithdrawRequestData(string paymentMethod, TradeQuantity qty)
    {
        PaymentMethod = paymentMethod;
        Qty = qty;
    }
}

#nullable enable
public record TradeQuantity
{
    public TradeQuantity(decimal value, ValueType type)
    {
        Type = type;
        Value = value;
    }

    public enum ValueType
    {
        Exact,
        Percent
    }

    public ValueType Type { get; }
    public decimal Value { get; set; }

    public override string ToString()
    {
        if (Type == ValueType.Exact)
            return Value.ToString(CultureInfo.InvariantCulture);
        else
            return Value.ToString(CultureInfo.InvariantCulture) + "%";
    }
    public static TradeQuantity Parse(string str)
    {
        if (!TryParse(str, out var r))
            throw new FormatException("Invalid TradeQuantity");
        return r;
    }
    public static bool TryParse(string str, [MaybeNullWhen(false)] out TradeQuantity quantity)
    {
        if (str is null)
            throw new ArgumentNullException(nameof(str));
        quantity = null;
        str = str.Trim();
        str = str.Replace(" ", "");
        if (str.Length == 0)
            return false;
        if (str[^1] == '%')
        {
            if (!decimal.TryParse(str[..^1], NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
                return false;
            if (r < 0.0m)
                return false;
            quantity = new TradeQuantity(r, TradeQuantity.ValueType.Percent);
        }
        else
        {
            if (!decimal.TryParse(str, NumberStyles.Any, CultureInfo.InvariantCulture, out var r))
                return false;
            if (r < 0.0m)
                return false;
            quantity = new TradeQuantity(r, TradeQuantity.ValueType.Exact);
        }
        return true;
    }
}
