using System;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.JsonConverters
{
    public class MnemonicJsonConverter : JsonConverter<Mnemonic>
    {
        public override Mnemonic ReadJson(JsonReader reader, Type objectType, Mnemonic existingValue, bool hasExistingValue,
            JsonSerializer serializer)
        {
            return reader.TokenType switch
            {
                JsonToken.String => new Mnemonic((string)reader.Value),
                JsonToken.Null => null,
                _ => throw new JsonObjectException(reader.Path, "Mnemonic must be a json string")
            };
        }

        public override void WriteJson(JsonWriter writer, Mnemonic value, JsonSerializer serializer)
        {
            if (value != null)
                writer.WriteValue(value.ToString());
            else
            {
                writer.WriteNull();
            }
        }
    }
}
