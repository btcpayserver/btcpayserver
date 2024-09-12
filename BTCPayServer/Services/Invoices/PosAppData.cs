using System;
using System.Globalization;
using BTCPayServer.Plugins.PointOfSale.Models;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Invoices;

public class PosAppData
{
    [JsonProperty(PropertyName = "cart")]
    public PosAppCartItem[] Cart { get; set; }

    [JsonProperty(PropertyName = "amounts")]
    public decimal[] Amounts { get; set; }

    [JsonProperty(PropertyName = "customAmount")]
    public decimal CustomAmount { get; set; }

    [JsonProperty(PropertyName = "discountPercentage")]
    public decimal DiscountPercentage { get; set; }

    [JsonProperty(PropertyName = "discountAmount")]
    public decimal DiscountAmount { get; set; }

    [JsonProperty(PropertyName = "tipPercentage")]
    public decimal TipPercentage { get; set; }

    [JsonProperty(PropertyName = "tip")]
    public decimal Tip { get; set; }

    [JsonProperty(PropertyName = "subTotal")]
    public decimal Subtotal { get; set; }

    [JsonProperty(PropertyName = "total")]
    public decimal Total { get; set; }
}

public class PosAppCartItem
{
    [JsonProperty(PropertyName = "id")]
    public string Id { get; set; }

    [JsonProperty(PropertyName = "price")]
    [JsonConverter(typeof(PosAppCartItemPriceJsonConverter))]
    public decimal Price { get; set; }

    [JsonProperty(PropertyName = "title")]
    public string Title { get; set; }

    [JsonProperty(PropertyName = "count")]
    public int Count { get; set; }

    [JsonProperty(PropertyName = "inventory")]
    public int? Inventory { get; set; }

    [JsonProperty(PropertyName = "image")]
    public string Image { get; set; }
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
                return token.ToObject<JObject>()?["value"]?.Value<decimal?>();
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
