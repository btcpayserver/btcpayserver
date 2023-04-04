using System;
using System.Globalization;
using BTCPayServer.Client.Models;
using BTCPayServer.Lightning;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.JsonConverters
{
    public class TradeQuantityJsonConverter : JsonConverter<TradeQuantity>
    {
        public override TradeQuantity ReadJson(JsonReader reader, Type objectType, TradeQuantity existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            switch (token.Type)
            {
                case JTokenType.Float:
                case JTokenType.Integer:
                case JTokenType.String:
                    if (TradeQuantity.TryParse(token.ToString(), out var q))
                        return q;
                    break;
                case JTokenType.Null:
                    return null;
            }
            throw new JsonObjectException("Invalid TradeQuantity, expected string. Expected: \"1.50\" or \"50%\"", reader);
        }

        public override void WriteJson(JsonWriter writer, TradeQuantity value, JsonSerializer serializer)
        {
            if (value is not null)
                writer.WriteValue(value.ToString());
        }
    }
}
