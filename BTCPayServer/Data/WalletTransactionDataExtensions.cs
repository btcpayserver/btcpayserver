#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Labels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json.Serialization;

namespace BTCPayServer.Data
{
    public class WalletTransactionInfo
    {
        public class LabelAssociatedData
        {
            public LabelAssociatedData(string label, string color)
            {
                Color = color;
                Label = label;
            }
            public string Color { get; set; }
            public List<(String Id, String Type, JObject? Value)> Metadata { get; set; } = new List<(String Id, String Type, JObject? Value)>();
            public LabelData LegacyMetadata
            {
                get
                {
                    foreach (var metadata in Metadata)
                    {
                        switch (Label)
                        {
                            case "payout":
                                var legacyPayoutLabel = new LegacyPayoutLabel();
                                foreach (var m in Metadata.Where(m => m.Type == "payout"))
                                {
                                    var ppid = m.Value?["pullPaymentId"]?.Value<string>() ?? "";
                                    if (!legacyPayoutLabel.PullPaymentPayouts.TryGetValue(ppid, out var payoutIds))
                                    {
                                        payoutIds = new List<string>();
                                        legacyPayoutLabel.PullPaymentPayouts.Add(ppid, payoutIds);
                                    }
                                    payoutIds.Add(m.Id);
                                }
                                return legacyPayoutLabel;
                            case "payjoin":
                                return new ReferenceLabel("payjoin", "payjoin");
                            case "payment-request":
                            case "app":
                            case "pj-exposed":
                            case "invoice":
                                return new ReferenceLabel(Label, metadata.Id);
                            default: continue;
                        }
                    }
                    return new RawLabel(Label);
                }
            }

            public string Label { get; }
        }

        public WalletTransactionInfo(WalletId walletId)
        {
            WalletId = walletId;
        }
        [JsonIgnore]
        public WalletId WalletId { get; }
        public string Comment { get; set; } = string.Empty;
        [JsonIgnore]
        public Dictionary<string, LabelAssociatedData> Labels { get; set; } = new Dictionary<string, LabelAssociatedData>();
        Dictionary<string, LabelData>? _LegacyLabels;
        [JsonIgnore]
        public Dictionary<string, LabelData> LegacyLabels
        {
            get
            {
                return _LegacyLabels ??= Labels.ToDictionary(l => l.Key, l => l.Value.LegacyMetadata);
            }
        }

    }
    public static class WalletTransactionDataExtensions
    {

        //public static WalletTransactionInfo GetBlobInfo(this WalletTransactionData walletTransactionData)
        //{
        //    WalletTransactionInfo blobInfo;
        //    if (walletTransactionData.Blob == null || walletTransactionData.Blob.Length == 0)
        //        blobInfo = new WalletTransactionInfo();
        //    else
        //        blobInfo = JsonConvert.DeserializeObject<WalletTransactionInfo>(ZipUtils.Unzip(walletTransactionData.Blob));
        //    if (!string.IsNullOrEmpty(walletTransactionData.Labels))
        //    {
        //        if (walletTransactionData.Labels.StartsWith('['))
        //        {
        //            foreach (var jtoken in JArray.Parse(walletTransactionData.Labels))
        //            {
        //                var l = jtoken.Type == JTokenType.String ? Label.Parse(jtoken.Value<string>())
        //                                                        : Label.Parse(jtoken.ToString());
        //                blobInfo.Labels.TryAdd(l.Text, l);
        //            }
        //        }
        //        else
        //        {
        //            // Legacy path
        //            foreach (var token in walletTransactionData.Labels.Split(',',
        //                StringSplitOptions.RemoveEmptyEntries))
        //            {
        //                var l = Label.Parse(token);
        //                blobInfo.Labels.TryAdd(l.Text, l);
        //            }
        //        }
        //    }
        //    return blobInfo;
        //}
        static JsonSerializerSettings LabelSerializerSettings = new JsonSerializerSettings()
        {
            ContractResolver = new CamelCasePropertyNamesContractResolver(),
            Formatting = Formatting.None
        };
        public static void SetBlobInfo(this WalletTransactionData walletTransactionData, WalletTransactionInfo blobInfo)
        {
            if (blobInfo == null)
            {
                walletTransactionData.Labels = string.Empty;
                walletTransactionData.Blob = Array.Empty<byte>();
                return;
            }
            walletTransactionData.Labels = new JArray(
                blobInfo.LegacyLabels.Select(l => JsonConvert.SerializeObject(l.Value, LabelSerializerSettings))
                .Select(l => JObject.Parse(l))
                .OfType<JToken>()
                .ToArray()).ToString();
            walletTransactionData.Blob = ZipUtils.Zip(JsonConvert.SerializeObject(blobInfo));
        }
    }
}
