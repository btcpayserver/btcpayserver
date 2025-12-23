using System;
using System.Reflection;
using BTCPayServer.Payouts;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.JsonConverters
{
    public class UnresolvedUriJsonConverter : JsonConverter<UnresolvedUri>
    {
        public override UnresolvedUri ReadJson(JsonReader reader, Type objectType, UnresolvedUri existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            if (reader.TokenType != JsonToken.String)
                throw new JsonObjectException("A UnresolvedUri should be a string", reader);
            var str = (string)reader.Value;
            if (str.Length == 0)
                return null;
            return UnresolvedUri.Create(str);
        }

        public override void WriteJson(JsonWriter writer, UnresolvedUri value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(value.ToString());
        }
    }
}
