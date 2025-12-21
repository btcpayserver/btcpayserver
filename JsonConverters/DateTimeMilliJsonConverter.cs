using System;
using System.Reflection;
using Newtonsoft.Json;

namespace BTCPayServer.JsonConverters
{
    class DateTimeMilliJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(DateTime).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo()) ||
                   typeof(DateTimeOffset).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo()) ||
                   typeof(DateTimeOffset?).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        static readonly DateTimeOffset unixRef = new DateTimeOffset(1970, 1, 1, 0, 0, 0, TimeSpan.Zero);
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.Value == null)
                return null;
            if (reader.TokenType == JsonToken.Null)
                return null;
            var result = UnixTimeToDateTime((ulong)(long)reader.Value);
            if (objectType == typeof(DateTime))
                return result.UtcDateTime;
            return result;
        }

        static DateTimeOffset UnixTimeToDateTime(ulong value)
        {
            var v = (long)value;
            if (v < 0)
                throw new FormatException("Invalid datetime (less than 1/1/1970)");
            return unixRef + TimeSpan.FromMilliseconds(v);
        }
        static long DateTimeToUnixTime(in DateTime time)
        {
            var date = ((DateTimeOffset)time).ToUniversalTime();
            long v = (long)(date - unixRef).TotalMilliseconds;
            if (v < 0)
                throw new FormatException("Invalid datetime (less than 1/1/1970)");
            return v;
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            DateTime time;
            if (value is DateTime)
                time = (DateTime)value;
            else
                time = ((DateTimeOffset)value).UtcDateTime;

            if (time < UnixTimeToDateTime(0))
                time = UnixTimeToDateTime(0).UtcDateTime;
            writer.WriteValue(DateTimeToUnixTime(time));
        }


    }
}
