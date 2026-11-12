using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using BTCPayServer.Plugins.PointOfSale;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Invoices;

public class PosAppData
{
    public static PosAppData TryParse(string posData)
    {
        try
        {
            return JsonParser.ParseObject(posData).ToObject<PosAppData>();
        }
        catch
        {
        }
        return null;
    }
    public static PosAppData TryParse(JObject posData)
    {
        try
        {
            return posData.ToObject<PosAppData>();
        }
        catch
        {
        }
        return null;
    }

    [JsonProperty(PropertyName = "cart")]
    public PosAppCartItem[] Cart { get; set; }

    [JsonProperty(PropertyName = "amounts")]
    public decimal[] Amounts { get; set; }

    [JsonProperty(PropertyName = "customAmount")]
    public decimal CustomAmount { get; set; }

    [JsonProperty(PropertyName = "discountPercentage")]
    public decimal DiscountPercentage { get; set; }

    [JsonProperty(PropertyName = "tipPercentage")]
    public decimal TipPercentage { get; set; }

    [JsonProperty(PropertyName = "itemsTotal")]
    public decimal ItemsTotal { get; set; }
    [JsonProperty(PropertyName = "discountAmount")]
    public decimal DiscountAmount { get; set; }
    [JsonProperty(PropertyName = "subTotal")]
    public decimal Subtotal { get; set; }
    [JsonProperty(PropertyName = "tax")]
    public decimal Tax { get; set; }
    [JsonProperty(PropertyName = "tip")]
    public decimal Tip { get; set; }
    [JsonProperty(PropertyName = "total")]
    public decimal Total { get; set; }

    internal void UpdateFrom(PoSOrder.OrderSummary summary)
    {
        ItemsTotal = summary.ItemsTotal;
        DiscountAmount = summary.Discount;
        Subtotal = summary.PriceTaxExcluded;
        Tax = summary.Tax;
        Tip = summary.Tip;
        Total = summary.PriceTaxIncludedWithTips;
    }

    internal void NormalizeCartItemOptions()
    {
        if (Cart is null)
            return;

        foreach (var item in Cart)
            item?.NormalizeOptions();
    }
}

public class PosAppCartItem
{
    public const int MaxOptions = 20;
    public const int MaxOptionLength = 200;

    [JsonProperty(PropertyName = "id")]
    public string Id { get; set; }

    [JsonProperty(PropertyName = "price")]
    [JsonConverter(typeof(PosAppCartItemPriceJsonConverter))]
    public decimal Price { get; set; }

    [JsonProperty(PropertyName = "title")]
    public string Title { get; set; }

    [JsonProperty(PropertyName = "options", NullValueHandling = NullValueHandling.Ignore)]
    public string[] Options { get; set; }

    [JsonProperty(PropertyName = "count")]
    public int Count { get; set; }

    [JsonProperty(PropertyName = "inventory")]
    public int? Inventory { get; set; }

    [JsonProperty(PropertyName = "image")]
    public string Image { get; set; }

    internal void NormalizeOptions()
    {
        if (Options is null)
            return;

        Options = Options
            .Select(NormalizeOption)
            .Where(o => o is not null)
            .Take(MaxOptions)
            .ToArray();
        if (Options.Length == 0)
            Options = null;
    }

    private static string NormalizeOption(string option)
    {
        if (option is null)
            return null;
        option = option.Trim();
        if (option.Length == 0)
            return null;
        return option.Length <= MaxOptionLength ? option : option[..MaxOptionLength];
    }

    public static string[] NormalizeOptionsList(IEnumerable<string> options)
    {
        if (options is null)
            return null;

        var normalized = options
            .Select(NormalizeOption)
            .Where(o => o is not null)
            .Take(MaxOptions)
            .ToArray();
        return normalized.Length == 0 ? null : normalized;
    }
}

public class PosAppCartItemPriceJsonConverter : JsonConverter
{
    public override bool CanConvert(Type objectType)
    {
        return objectType == typeof(decimal) || objectType == typeof(object);
    }

    public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
        JsonSerializer serializer)
    {
        JToken token = JToken.Load(reader);
        switch (token.Type)
        {
            case JTokenType.Float:
                if (objectType == typeof(decimal))
                    return token.Value<decimal>();
                throw new JsonSerializationException($"Unexpected object type: {objectType}");
            case JTokenType.Integer:
            case JTokenType.String:
                if (objectType == typeof(decimal))
                    return decimal.Parse(token.ToString(), NumberStyles.Any, CultureInfo.InvariantCulture);
                throw new JsonSerializationException($"Unexpected object type: {objectType}");
            case JTokenType.Null:
                return null;
            case JTokenType.Object:
                return token.ToObject<JObject>()?["value"]?.Value<decimal?>() ?? (objectType == typeof(decimal) ? 0m : null);
            default:
                throw new JsonSerializationException($"Unexpected token type: {token.Type}");
        }
    }

    public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
    {
        if (value is decimal x)
            writer.WriteValue(x);
    }
}
