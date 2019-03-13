using System;
using System.Reflection;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using NBitcoin.JsonConverters;
using BTCPayServer.Rating;

namespace BTCPayServer.JsonConverters
{
    public class CurrencyPairJsonConverter : JsonConverter
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
