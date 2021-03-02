using System;
using System.Globalization;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.JsonConverters
{
    public class NumericStringJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(decimal) ||
                   objectType == typeof(decimal?) ||
                   objectType == typeof(double) ||
                   objectType == typeof(double?) ||
                   objectType == typeof(int) ||
                   objectType == typeof(int?);
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
                    if (objectType == typeof(decimal) || objectType == typeof(decimal?) )
                        return decimal.Parse(token.ToString(), CultureInfo.InvariantCulture);
                    if (objectType == typeof(double) || objectType == typeof(double?))
                        return double.Parse(token.ToString(), CultureInfo.InvariantCulture);
                    if (objectType == typeof(int) || objectType == typeof(int?))
                        return int.Parse(token.ToString(), CultureInfo.InvariantCulture);

                    throw new JsonSerializationException("Unexpected object type: " + objectType);
                case JTokenType.Null when objectType == typeof(decimal?) || objectType == typeof(double?) || objectType == typeof(int?):
                    return null;
                default:
                    throw new JsonSerializationException("Unexpected token type: " +
                                                         token.Type);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            switch (value)
            {
                case null:
                    break;
                case decimal x:
                    writer.WriteValue(x.ToString(CultureInfo.InvariantCulture));
                    break;
                case double x:
                    writer.WriteValue(x.ToString(CultureInfo.InvariantCulture));
                    break;
                case int x:
                    writer.WriteValue(x.ToString(CultureInfo.InvariantCulture));
                    break;
            }
        }
    }
}
