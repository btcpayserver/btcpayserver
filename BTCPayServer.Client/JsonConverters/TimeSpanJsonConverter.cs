using System;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.JsonConverters
{
    public class TimeSpanJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(TimeSpan) || objectType == typeof(TimeSpan?);
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                var nullable = objectType == typeof(TimeSpan?);
                if (reader.TokenType == JsonToken.Null)
                {
                    if (nullable)
                        return null;
                    return TimeSpan.Zero;
                }
                if (reader.TokenType != JsonToken.Integer)
                    throw new JsonObjectException("Invalid timespan, expected integer", reader);
                return TimeSpan.FromSeconds((long)reader.Value);
            }
            catch
            {
                throw new JsonObjectException("Invalid locktime", reader);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is TimeSpan s)
            {
                writer.WriteValue((long)s.TotalSeconds);
            }
        }
    }
}
