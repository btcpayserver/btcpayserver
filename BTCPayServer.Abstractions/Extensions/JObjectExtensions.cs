using System;
using System.Linq;
using System.Reflection;
using NBitcoin.RPC;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Abstractions.Extensions;

public static class JObjectExtensions
{
    public static string? GetValueByPath(this JObject json, string path)
    {
        string[] pathParts = path.Split('.');

        JObject loopedJObject = json;
        var last = pathParts.Last();
        string? r = null;
        
        foreach (string pathPart in pathParts)
        {
            var isLast = pathPart.Equals(last);

            var childNode = loopedJObject.GetValue(pathPart);
            if (isLast)
            {
                // Set the value now we're reached the final node
                r = loopedJObject.GetValue(pathPart)?.ToString();
            }
            else if (childNode == null)
            {
                r = null;
            }
            else if (childNode is JObject childNodeJObject)
            {
                loopedJObject = childNodeJObject;
            }
            else
            {
                throw new Exception("Cannot move into key '" + path + "' in JObject " + json);
            }
        }

        return r?.ToString();
    }

    public static void SetValueByPath(this JObject json, string path, string value)
    {
        string[] pathParts = path.Split('.');

        JObject loopedJObject = json;
        var last = pathParts.Last();
        foreach (string pathPart in pathParts)
        {
            var isLast = pathPart.Equals(last);

            var childNode = loopedJObject.GetValue(pathPart);
            if (isLast)
            {
                // Set the value now we're reached the final node
                loopedJObject.Add(pathPart, value);
            }
            else if (childNode == null)
            {
                var childNodeJObj = new JObject();
                loopedJObject.Add(pathPart, childNodeJObj);
                loopedJObject = childNodeJObj;
            }
            else if (childNode is JObject childNodeJObject)
            {
                loopedJObject = childNodeJObject;
            }
            else
            {
                throw new Exception("Cannot move into key '" + path + "' in JObject " + json);
            }
        }
    }
}
