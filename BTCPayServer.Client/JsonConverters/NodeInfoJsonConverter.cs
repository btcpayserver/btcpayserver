using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using System.Reflection;
using BTCPayServer.Lightning;
using Newtonsoft.Json;
using NBitcoin.JsonConverters;

namespace BTCPayServer.Client.JsonConverters
{
    public class NodeInfoJsonConverter : JsonConverter
    {
        public override bool CanConvert(Type objectType)
        {
            return typeof(NodeInfo).GetTypeInfo().IsAssignableFrom(objectType.GetTypeInfo());
        }

        public override object ReadJson(JsonReader reader, Type objectType, object existingValue, JsonSerializer serializer)
        {
            if (reader.TokenType == JsonToken.Null)
                return null;
            if (reader.TokenType != JsonToken.String)
                throw new JsonObjectException("String expected for Node URI", reader);
            if (NodeInfo.TryParse((string)reader.Value, out var ni))
                return ni;
            throw new JsonObjectException("Invalid Node URI", reader);
        }

        public override void WriteJson(JsonWriter writer, object value, JsonSerializer serializer)
        {
            if (value is NodeInfo ni)
                writer.WriteValue(ni.ToString());
        }
    }
}
