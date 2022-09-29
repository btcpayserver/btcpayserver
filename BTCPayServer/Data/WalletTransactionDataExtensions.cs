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
            public LabelAssociatedData(string label)
            {
                Label = label;
            }
            public string Color { get; set; }
            public List<LabelData> Metadata { get; set; } = new List<LabelData>();
            public LabelData LegacyMetadata
            {
                get
                {
                    var legacy = Metadata.FirstOrDefault() ?? new RawLabel(Label);
                    // Before, the payout label were merged into a single one. Only the LabelFactory use this
                    // so we might want to remove it soon.
                    if (legacy is PayoutLabel)
                    {
                        var legacyPayoutLabel = new LegacyPayoutLabel();
                        foreach (var metadata in Metadata.OfType<PayoutLabel>())
                        {
                            var ppid = metadata.PullPaymentId ?? "";
                            if (!legacyPayoutLabel.PullPaymentPayouts.TryGetValue(ppid, out var payoutIds))
                            {
                                payoutIds = new List<string>();
                                legacyPayoutLabel.PullPaymentPayouts.Add(ppid, payoutIds);
                            }
                            payoutIds.Add(metadata.PayoutId);
                        }
                        legacy = legacyPayoutLabel;
                    }
                    return legacy;
                }
            }

            public string Label { get; }
        }
        public string Comment { get; set; } = string.Empty;
        [JsonIgnore]
        public Dictionary<string, LabelAssociatedData> Labels { get; set; } = new Dictionary<string, LabelAssociatedData>();
        Dictionary<string, LabelData> _LegacyLabels;
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
