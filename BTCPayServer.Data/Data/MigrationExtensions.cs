#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO.Compression;
using System.IO;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Data
{
    public static class MigrationExtensions
    {
        public static JProperty? Move(this JObject blob, string[] pathFrom, string[] pathTo)
        {
            var from = GetProperty(blob, pathFrom, false);
            if (from is null)
                return null;
            var to = GetProperty(blob, pathTo, true);
            to!.Value = from.Value;
            from.Remove();
            return to;
        }

        public static void RenameProperty(this JObject o, string oldName, string newName)
        {
            var p = o.Property(oldName);
            if (p is null)
                return;
            RenameProperty(ref p, newName);
        }
        public static void RenameProperty(ref JProperty ls, string newName)
        {
            if (ls.Name != newName)
            {
                var parent = ls.Parent;
                ls.Remove();
                ls = new JProperty(newName, ls.Value);
                parent!.Add(ls);
            }
        }

        public static JProperty? GetProperty(this JObject blob, string[] pathFrom, bool createIfNotExists)
        {
            var current = blob;
            for (int i = 0; i < pathFrom.Length - 1; i++)
            {
                if (current.TryGetValue(pathFrom[i], out var value) && value is JObject jObject)
                {
                    current = jObject;
                }
                else
                {
                    if (!createIfNotExists)
                        return null;
                    JProperty? prop = null;
                    for (int ii = i; ii < pathFrom.Length; ii++)
                    {
                        var newProp = new JProperty(pathFrom[ii], new JObject());
                        if (prop is null)
                            current.Add(newProp);
                        else
                            prop.Value = new JObject(newProp);
                        prop = newProp;
                    }
                    return prop;
                }
            }
            var result = current.Property(pathFrom[pathFrom.Length - 1]);
            if (result is null && createIfNotExists)
            {
                result = new JProperty(pathFrom[pathFrom.Length - 1], null as object);
                current.Add(result);
            }
            return result;
        }
        public static CamelCaseNamingStrategy Camel = new CamelCaseNamingStrategy();
        public static void RemoveIfNull(this JObject blob, string propName)
        {
            if (blob.Property(propName)?.Value.Type is JTokenType.Null)
                blob.Remove(propName);
        }
        public static void RemoveIfValue<T>(this JObject conf, string propName, T v)
        {
            var p = conf.Property(propName);
            if (p is null)
                return;
            if (p.Value is JValue { Type: JTokenType.Null })
            {
                if (EqualityComparer<T>.Default.Equals(default, v))
                    p.Remove();
            }
            else if (p.Value is JValue jv)
            {
                if (EqualityComparer<T>.Default.Equals(jv.Value<T>(), v))
                {
                    p.Remove();
                }
            }
        }

        public static void ConvertNumberToString(this JObject o, string prop)
        {
            if (o[prop]?.Type is JTokenType.Float)
                o[prop] = o[prop]!.Value<decimal>().ToString(CultureInfo.InvariantCulture);
            if (o[prop]?.Type is JTokenType.Integer)
                o[prop] = o[prop]!.Value<long>().ToString(CultureInfo.InvariantCulture);
        }
        public static string Unzip(byte[] bytes)
        {
            MemoryStream ms = new MemoryStream(bytes);
            using GZipStream gzip = new GZipStream(ms, CompressionMode.Decompress);
            StreamReader reader = new StreamReader(gzip, Encoding.UTF8);
            var unzipped = reader.ReadToEnd();
            return unzipped;
        }

        public static int GetDivisibility(string paymentMethodId)
        {
            var splitted = paymentMethodId.Split('-');
            return (CryptoCode: splitted[0], Type: splitted[1]) switch
            {
                { Type: "LN" } or { Type: "LNURL" } => 11,
                { Type: "CHAIN", CryptoCode: var code } when code == "XMR" => 12,
                { Type: "CHAIN" } => 8,
                _ => 8
            };
        }

        public static string MigratePaymentMethodId(string paymentMethodId)
        {
            var splitted = paymentMethodId.Split(new[] { '_', '-' });
            if (splitted is [var cryptoCode, var paymentType])
            {
                return paymentType switch
                {
                    "BTCLike" => $"{cryptoCode}-CHAIN",
                    "LightningLike" or "LightningNetwork" => $"{cryptoCode}-LN",
                    "LNURLPAY" => $"{cryptoCode}-LNURL",
                    _ => throw new NotSupportedException("Unknown payment type " + paymentType)
                };
            }
            if (splitted.Length == 1)
                return $"{splitted[0]}-CHAIN";
            throw new NotSupportedException("Unknown payment id " + paymentMethodId);
        }
        public static string TryMigratePaymentMethodId(string paymentMethodId)
        {
            var splitted = paymentMethodId.Split(new[] { '_', '-' });
            if (splitted is [var cryptoCode, var paymentType])
            {
                return paymentType switch
                {
                    "BTCLike" => $"{cryptoCode}-CHAIN",
                    "LightningLike" or "LightningNetwork" => $"{cryptoCode}-LN",
                    "LNURLPAY" => $"{cryptoCode}-LNURL",
                    _ => paymentMethodId
                };
            }
            if (splitted.Length == 1)
                return $"{splitted[0]}-CHAIN";
            return paymentMethodId;
        }

        // Make postgres happy
        public static string SanitizeJSON(string json) => json.Replace("\\u0000", string.Empty, StringComparison.OrdinalIgnoreCase);
    }
}
