using System;
using System.Globalization;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Client.JsonConverters
{
    public class MoneyJsonConverter : NBitcoin.JsonConverters.MoneyJsonConverter
    {
        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.String)
            {
                return new Money(long.Parse((string)reader.Value));
            }
            return base.ReadJson(reader, objectType, existingValue, serializer);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(((Money)value).Satoshi.ToString(CultureInfo.InvariantCulture));
        }
    }
}
