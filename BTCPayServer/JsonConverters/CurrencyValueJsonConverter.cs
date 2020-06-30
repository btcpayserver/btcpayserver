using System;
using System.Reflection;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.JsonConverters
{
    public class CurrencyValueJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(CurrencyValue).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                return reader.TokenType == JsonToken.Null ? null :
                       CurrencyValue.TryParse((string)reader.Value, out var result) ? result :
                       throw new JsonObjectException("Invalid Currency value", reader);
            }
            catch (InvalidCastException)
            {
                throw new JsonObjectException("Invalid Currency value", reader);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(value.ToString());
        }
    }
}
