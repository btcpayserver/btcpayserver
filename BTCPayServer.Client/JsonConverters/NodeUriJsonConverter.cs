using System;
using System.Diagnostics.CodeAnalysis;
using BTCPayServer.Lightning;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.JsonConverters
{
    public class NodeUriJsonConverter : JsonConverter<NodeInfo>
    {
        public override NodeInfo ReadJson(JsonReader reader, Type objectType, [AllowNull] NodeInfo existingValue, bool hasExistingValue, JsonSerializer serializer)
        {
            if (reader.TokenType != JsonToken.String)
                throw new JsonObjectException(reader.Path, "Unexpected token type for NodeUri");
            if (NodeInfo.TryParse((string)reader.Value, out var info))
                return info;
            throw new JsonObjectException(reader.Path, "Invalid NodeUri");
        }

        public override void WriteJson(JsonWriter writer, [AllowNull] NodeInfo value, JsonSerializer serializer)
        {
            if (value is not null)
                writer.WriteValue(value.ToString());
        }
    }
}
