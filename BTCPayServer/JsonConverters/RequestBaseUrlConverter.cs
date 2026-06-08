using System;
using BTCPayServer.Abstractions;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.JsonConverters
{
    public class RequestBaseUrlConverter : JsonConverter<RequestBaseUrl>
    {
        public override RequestBaseUrl ReadJson(JsonReader reader, Type objectType, RequestBaseUrl existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            if (reader.TokenType != JsonToken.String)
                throw new JsonObjectException("A request base URL should be a string", reader);
            if (RequestBaseUrl.TryFromUrl((string)reader.Value, out var result))
                return result;
            throw new JsonObjectException("Invalid request base URL", reader);
        }

        public override void WriteJson(JsonWriter writer, RequestBaseUrl value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(value.ToString());
        }
    }
}
