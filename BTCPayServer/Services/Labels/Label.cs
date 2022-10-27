using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Labels
{

    [Obsolete]
    public abstract class Label : LabelData
    {
        static void FixLegacy(JObject jObj, ReferenceLabel refLabel)
        {
            if (refLabel.Reference is null && jObj.ContainsKey("id"))
                refLabel.Reference = jObj["id"].Value<string>();
            FixLegacy(jObj, (Label)refLabel);
        }
        static void FixLegacy(JObject jObj, PayoutLabel payoutLabel)
        {
            if (jObj.ContainsKey("id") && payoutLabel.PullPaymentPayouts.Count is 0)
            {
                var pullPaymentId = jObj["pullPaymentId"]?.Value<string>() ?? string.Empty;
                payoutLabel.PullPaymentPayouts.Add(pullPaymentId, new List<string>() { jObj["id"].Value<string>() });
            }
            else if (jObj.ContainsKey("payoutId") && jObj.ContainsKey("pullPaymentId"))
            {
                var pullPaymentId = jObj["pullPaymentId"]?.Value<string>() ?? string.Empty;
                var payoutId = jObj["payoutId"]?.Value<string>() ?? string.Empty;
                payoutLabel.PullPaymentPayouts.Add(pullPaymentId, new List<string>() { payoutId });
            }
            FixLegacy(jObj, (Label)payoutLabel);
        }
        static void FixLegacy(JObject jObj, Label label)
        {
            if (label.Type is null)
                label.Type = jObj["value"].Value<string>();
            if (label.Text is null)
                label.Text = label.Type;
        }
        static void FixLegacy(JObject jObj, RawLabel rawLabel)
        {
            rawLabel.Type = "raw";
            FixLegacy(jObj, (Label)rawLabel);
        }

        static Label()
        {
            SerializerSettings = new JsonSerializerSettings()
            {
                ContractResolver = new Newtonsoft.Json.Serialization.CamelCasePropertyNamesContractResolver()
            };
            Serializer = JsonSerializer.Create(SerializerSettings);
        }

        public static JsonSerializerSettings SerializerSettings;
        public static JsonSerializer Serializer;
        public static Label Parse(string str)
        {
            ArgumentNullException.ThrowIfNull(str);
            if (str.StartsWith("{", StringComparison.InvariantCultureIgnoreCase))
            {
                var jObj = JObject.Parse(str);
                string type = null;
                // Legacy label
                if (!jObj.ContainsKey("type"))
                {
                    type = jObj["value"].Value<string>();
                }
                else
                {
                    type = jObj["type"].Value<string>();
                }

                switch (type)
                {
                    case "raw":
                        var rawLabel = JsonConvert.DeserializeObject<RawLabel>(str);
                        FixLegacy(jObj, rawLabel);
                        return rawLabel;
                    case "invoice":
                    case "payment-request":
                    case "app":
                    case "pj-exposed":
                        var refLabel = JsonConvert.DeserializeObject<ReferenceLabel>(str);
                        FixLegacy(jObj, refLabel);
                        return refLabel;
                    case "payout":
                        var payoutLabel = JsonConvert.DeserializeObject<PayoutLabel>(str);
                        FixLegacy(jObj, payoutLabel);
                        return payoutLabel;
                    default:
                        // Legacy
                        return new RawLabel(jObj["value"].Value<string>());
                }
            }
            else
            {
                return new RawLabel(str);
            }
        }
    }

    [Obsolete]
    public class RawLabel : Label
    {
        public RawLabel()
        {
            Type = "raw";
        }
        public RawLabel(string text) : this()
        {
            Text = text;
        }
    }
    [Obsolete]
    public class ReferenceLabel : Label
    {
        public ReferenceLabel()
        {

        }
        public ReferenceLabel(string type, string reference)
        {
            Text = type;
            Reference = reference;
            Type = type;
        }
        [JsonProperty("ref")]
        public string Reference { get; set; }
    }
    [Obsolete]
    public class PayoutLabel : Label
    {
        public PayoutLabel()
        {
            Type = "payout";
            Text = "payout";
        }

        public Dictionary<string, List<string>> PullPaymentPayouts { get; set; } = new();
    }
}
