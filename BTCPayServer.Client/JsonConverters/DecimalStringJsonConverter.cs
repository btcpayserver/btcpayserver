using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.JsonConverters
{
    public class DecimalStringJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return (objectType == typeof(decimal) || objectType == typeof(decimal?));
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue,
            JsonSerializer serializer)
        {
            JToken token = JToken.Load(reader);
            switch (token.Type)
            {
                case JTokenType.Float:
                case JTokenType.Integer:
                case JTokenType.String:
                    return decimal.Parse(token.ToString(), CultureInfo.InvariantCulture);
                case JTokenType.Null when objectType == typeof(decimal?):
                    return null;
                default:
                    throw new JsonSerializationException("Unexpected token type: " +
                                                         token.Type);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(((decimal)value).ToString(CultureInfo.InvariantCulture));
        }
    }
}
