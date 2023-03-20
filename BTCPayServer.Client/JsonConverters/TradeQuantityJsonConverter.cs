using System;
using System.Globalization;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.JsonConverters
{
    public class TradeQuantityJsonConverter : JsonConverter<TradeQuantity>
    {
        public override TradeQuantity ReadJson(JsonReader reader, Type objectType, TradeQuantity existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            if (reader.TokenType != JsonToken.String)
                throw new JsonObjectException("Invalid TradeQuantity, expected string. Expected: \"1.50\" or \"50%\"", reader);
            if (TradeQuantity.TryParse((string)reader.Value, out var q))
                return q;
            throw new JsonObjectException("Invalid format for TradeQuantity. Expected: \"1.50\" or \"50%\"", reader);
        }

        public override void WriteJson(JsonWriter writer, TradeQuantity value, JsonSerializer serializer)
        {
            if (value is not null)
                writer.WriteValue(value.ToString());
        }
    }
}
