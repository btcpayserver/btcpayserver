using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace BTCPayServer.Abstractions.Converters
{
    public class VersionConverter : JsonConverter<Version>
    {
        public override Version Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            return Version.Parse(reader.GetString());
        }

        public override void Write(Utf8JsonWriter writer, Version value, JsonSerializerOptions options)
        {
            writer.WriteStringValue(value.ToString());
        }
    }
}
