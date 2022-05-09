using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using NBitcoin;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Labels
{

    public abstract class Label : LabelData
    {
        public virtual Label Merge(LabelData old)
        {
            return this;
        }
        
        static void FixLegacy(JObject jObj, ReferenceLabel refLabel)
        {
            if (refLabel.Reference is null && jObj.ContainsKey("id"))
                refLabel.Reference = jObj["id"].Value<string>();
            FixLegacy(jObj, (Label)refLabel);
        }
        static void FixLegacy(JObject jObj, PayoutLabel payoutLabel)
        {
            if (payoutLabel.PullPaymentPayouts is null && payoutLabel.PayoutId is null)
                payoutLabel.PayoutId = jObj["id"].Value<string>();
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
    public class PayoutLabel : Label
    {
        public PayoutLabel()
        {
            Type = "payout";
            Text = "payout";
        }
        [Obsolete]
        public string PayoutId { get; set; }
        [Obsolete]
        public string PullPaymentId { get; set; }

        public Dictionary<string, string[]> PullPaymentPayouts { get; set; } = new();
        public string WalletId { get; set; }

        public override Label Merge(LabelData old)
        {
            if (old is not PayoutLabel payoutLabelOld) return base.Merge(old);
            payoutLabelOld.PullPaymentPayouts ??= new()
            {
                {payoutLabelOld.PullPaymentId ?? "", new[] {payoutLabelOld.PayoutId}}
            };

            foreach (KeyValuePair<string,string[]> pullPaymentPayout in payoutLabelOld.PullPaymentPayouts)
            {
                if (!PullPaymentPayouts.TryGetValue(pullPaymentPayout.Key, out var pullPaymentPayouts))
                {
                    pullPaymentPayouts = Array.Empty<string>();
                }
                pullPaymentPayouts = pullPaymentPayouts.Concat(pullPaymentPayout.Value).ToArray();
                PullPaymentPayouts.AddOrReplace(pullPaymentPayout.Key, pullPaymentPayouts);
            }
            return base.Merge(old);
        }
    }
}
