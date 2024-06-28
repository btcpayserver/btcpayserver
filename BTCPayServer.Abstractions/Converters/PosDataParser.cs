#nullable enable
using System.Collections.Generic;
using System.Linq;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Abstractions.Converters;

public static class PosDataParser
{
    public static Dictionary<string, object> ParsePosData(JToken? posData)
    {
        var result = new Dictionary<string, object>();
        if (posData is JObject jobj)
        {
            foreach (var item in jobj)
            {
                ParsePosDataItem(item, ref result);
            }
        }
        return result;
    }

    static void ParsePosDataItem(KeyValuePair<string, JToken?> item, ref Dictionary<string, object> result)
    {
        switch (item.Value?.Type)
        {
            case JTokenType.Array:
                var items = item.Value.AsEnumerable().ToList();
                var arrayResult = new List<object>();
                for (var i = 0; i < items.Count; i++)
                {
                    arrayResult.Add(items[i] is JObject
                        ? ParsePosData(items[i])
                        : items[i].ToString());
                }

                result.TryAdd(item.Key, arrayResult);

                break;
            case JTokenType.Object:
                result.TryAdd(item.Key, ParsePosData(item.Value));
                break;
            case null:
                break;
            default:
                result.TryAdd(item.Key, item.Value.ToString());
                break;
        }
    }
}
