using System;
using System.Globalization;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.JsonConverters
{
    public abstract class TimeSpanJsonConverter : JsonConverter
    {
        public class Seconds : TimeSpanJsonConverter
        {
            protected override long ToLong(TimeSpan value)
            {
                return (long)value.TotalSeconds;
            }

            protected override TimeSpan ToTimespan(long value)
            {
                return TimeSpan.FromSeconds(value);
            }
        }
        public class Minutes : TimeSpanJsonConverter
        {
            protected override long ToLong(TimeSpan value)
            {
                return (long)value.TotalMinutes;
            }
            protected override TimeSpan ToTimespan(long value)
            {
                return TimeSpan.FromMinutes(value);
            }
        }
        public class Days : TimeSpanJsonConverter
        {
            protected override long ToLong(TimeSpan value)
            {
                return (long)value.TotalDays;
            }
            protected override TimeSpan ToTimespan(long value)
            {
                return TimeSpan.FromDays(value);
            }
        }
        public override bool CanConvert(Type objectType)
        {
            return objectType == typeof(TimeSpan) || objectType == typeof(TimeSpan?);
        }

        protected abstract TimeSpan ToTimespan(long value);
        protected abstract long ToLong(TimeSpan value);
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
                if (reader.TokenType == JsonToken.String && TimeSpan.TryParse(reader.Value?.ToString(), CultureInfo.InvariantCulture, out var res))
                    return res;
                if (reader.TokenType != JsonToken.Integer)
                    throw new JsonObjectException("Invalid timespan, expected integer", reader);
                return ToTimespan((long)reader.Value);
            }
            catch
            {
                throw new JsonObjectException("Invalid timespan", reader);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is TimeSpan s)
            {
                writer.WriteValue(ToLong(s));
            }
        }
    }
}
