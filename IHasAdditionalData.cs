using System;
using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer
{
    public static class HasAdditionalDataExtensions
    {
        public static T GetAdditionalData<T>(this IHasAdditionalData o, string propName)
        {
            if (o.AdditionalData == null || !(o.AdditionalData.TryGetValue(propName, out var jt) is true))
                return default;
            if (jt.Type == JTokenType.Null)
                return default;
            if (typeof(T) == typeof(string))
            {
                return (T)(object)jt.ToString();
            }

            try
            {
                return jt.Value<T>();
            }
            catch (Exception)
            {
                return default;
            }
        }
        public static void SetAdditionalData<T>(this IHasAdditionalData o, string propName, T value)
        {
            JToken data;
            if (typeof(T) == typeof(string) && value is string v)
            {
                data = new JValue(v);
                o.AdditionalData ??= new Dictionary<string, JToken>();
                o.AdditionalData.AddOrReplace(propName, data);
                return;
            }
            if (value is null)
            {
                o.AdditionalData?.Remove(propName);
            }
            else
            {
                try
                {
                    if (value is string s)
                    {
                        data = JToken.Parse(s);
                    }
                    else
                    {
                        data = JToken.FromObject(value);
                    }
                }
                catch (Exception)
                {
                    data = JToken.FromObject(value);
                }

                o.AdditionalData ??= new Dictionary<string, JToken>();
                o.AdditionalData.AddOrReplace(propName, data);
            }
        }
    }
    public interface IHasAdditionalData
    {
        IDictionary<string, JToken> AdditionalData { get; set; }
    }
}
