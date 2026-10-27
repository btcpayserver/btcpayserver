#nullable enable

using System;
using System.IO;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer;

/// <summary>
/// Parses arbitrary JSON without converting date-like strings into date tokens.
/// </summary>
public static class JsonParser
{
    public const int DefaultMaxDepth = 64;

    public static JObject ParseObject(string json, int maxDepth = DefaultMaxDepth)
        => Load(json, maxDepth, JObject.Load);

    public static JArray ParseArray(string json, int maxDepth = DefaultMaxDepth)
        => Load(json, maxDepth, JArray.Load);

    public static JToken ParseToken(string json, int maxDepth = DefaultMaxDepth)
        => Load(json, maxDepth, JToken.Load);

    private static T Load<T>(string json, int maxDepth, Func<JsonReader, T> load)
    {
        ArgumentNullException.ThrowIfNull(json);
        ArgumentOutOfRangeException.ThrowIfNegativeOrZero(maxDepth);

        using var stringReader = new StringReader(json);
        using var jsonReader = new JsonTextReader(stringReader)
        {
            DateParseHandling = DateParseHandling.None,
            MaxDepth = maxDepth
        };
        var result = load(jsonReader);
        while (jsonReader.Read())
        {
            if (jsonReader.TokenType is not JsonToken.Comment)
                throw new JsonReaderException("Additional text encountered after finished reading JSON content.");
        }
        return result;
    }
}
