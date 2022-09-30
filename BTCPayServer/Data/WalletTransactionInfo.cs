#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Services;
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
            public List<TransactionTag> Tags { get; set; } = new List<TransactionTag>();
            public LabelData LegacyLabel
            {
                get
                {
                    foreach (var tag in Tags)
                    {
                        switch (Label)
                        {
                            case "payout":
                                var legacyPayoutLabel = new LegacyPayoutLabel();
                                foreach (var t in Tags.Where(m => m.Label == "payout"))
                                {
                                    var ppid = t.AssociatedData?["pullPaymentId"]?.Value<string>() ?? "";
                                    if (!legacyPayoutLabel.PullPaymentPayouts.TryGetValue(ppid, out var payoutIds))
                                    {
                                        payoutIds = new List<string>();
                                        legacyPayoutLabel.PullPaymentPayouts.Add(ppid, payoutIds);
                                    }
                                    payoutIds.Add(t.Id);
                                }
                                return legacyPayoutLabel;
                            case "payjoin":
                                return new ReferenceLabel("payjoin", "payjoin");
                            case "payment-request":
                            case "app":
                            case "pj-exposed":
                            case "invoice":
                                if (tag.Id.Length == 0)
                                    return new RawLabel(Label);
                                return new ReferenceLabel(Label, tag.Id);
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
        public List<TransactionTag> Tags { get; set; } = new List<TransactionTag>();

        [JsonIgnore]
        public Dictionary<string, string> LabelColors { get; set; } = new Dictionary<string, string>();

        Dictionary<string, LabelData>? _LegacyLabels;
        [JsonIgnore]
        public Dictionary<string, LabelData> LegacyLabels
        {
            get
            {
                if (_LegacyLabels is null)
                {
                    var legacyLabels = new Dictionary<string, LabelData>();
                    foreach (var tag in Tags)
                    {
                        switch (tag.Label)
                        {
                            case "payout":
                                LegacyPayoutLabel legacyPayoutLabel;
                                if (legacyLabels.TryGetValue(tag.Label, out var existing) &&
                                    existing is LegacyPayoutLabel)
                                {
                                    legacyPayoutLabel = (LegacyPayoutLabel)existing;
                                }
                                else
                                {
                                    legacyPayoutLabel = new LegacyPayoutLabel();
                                    legacyLabels.Add(tag.Label, legacyPayoutLabel);
                                }
                                var ppid = tag.AssociatedData?["pullPaymentId"]?.Value<string>() ?? "";
                                if (!legacyPayoutLabel.PullPaymentPayouts.TryGetValue(ppid, out var payouts))
                                {
                                    payouts = new List<string>();
                                    legacyPayoutLabel.PullPaymentPayouts.Add(ppid, payouts);
                                }
                                payouts.Add(tag.Id);
                                break;
                            case "payjoin":
                            case "payment-request":
                            case "app":
                            case "pj-exposed":
                            case "invoice":
                                legacyLabels.TryAdd(tag.Label, new ReferenceLabel(tag.Label, tag.Id));
                                break;
                            default:
                                continue;
                        }
                    }
                    foreach (var label in LabelColors)
                    {
                        legacyLabels.TryAdd(label.Key, new RawLabel(label.Key));
                    }
                    _LegacyLabels = legacyLabels;
                }
                return _LegacyLabels;
            }
        }

    }
}
