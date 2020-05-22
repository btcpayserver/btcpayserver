using System;
using System.Reflection;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Rating.JsonConverters
{
    public class CurrencyPairJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(CurrencyPair).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                return reader.TokenType == JsonToken.Null ? null :
                       CurrencyPair.TryParse((string)reader.Value, out var result) ? result :
                       throw new JsonObjectException("Invalid currency pair", reader);
            }
            catch (InvalidCastException)
            {
                throw new JsonObjectException("Invalid currency pair", reader);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(value.ToString());
        }
    }
}
