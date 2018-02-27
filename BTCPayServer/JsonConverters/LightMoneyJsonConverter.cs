using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json;
using System.Reflection;
using BTCPayServer.Payments.Lightning;
using NBitcoin.JsonConverters;
using System.Globalization;

namespace BTCPayServer.JsonConverters
{
    public class LightMoneyJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(LightMoneyJsonConverter).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            try
            {
                return reader.TokenType == JsonToken.Null ? null :
                    reader.TokenType == JsonToken.Integer ? new LightMoney((long)reader.Value) :
                    reader.TokenType == JsonToken.String ? new LightMoney(long.Parse((string)reader.Value, CultureInfo.InvariantCulture)) 
                    : null;
            }
            catch (InvalidCastException)
            {
                throw new JsonObjectException("Money amount should be in millisatoshi", reader);
            }
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            writer.WriteValue(((LightMoney)value).MilliSatoshi);
        }
    }
}
