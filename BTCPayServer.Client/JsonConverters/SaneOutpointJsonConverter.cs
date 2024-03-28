using NBitcoin;
using NBitcoin.DataEncoders;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using System;
using System.IO;
using System.Reflection;

namespace BTCPayServer.Client.JsonConverters
{
    public class SaneOutpointJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(OutPoint).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;

            if (reader.TokenType != JsonToken.String)
                throw new JsonObjectException($"Unexpected json token type, expected is {JsonToken.String} and actual is {reader.TokenType}", reader);
            try
            {
                if (!OutPoint.TryParse((string)reader.Value, out var outpoint))
                    throw new JsonObjectException("Invalid bitcoin object of type OutPoint", reader);
                return outpoint;
            }
            catch (EndOfStreamException)
            {
            }
            throw new JsonObjectException("Invalid bitcoin object of type OutPoint", reader);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is { })
                writer.WriteValue(value.ToString());
        }
    }
}
