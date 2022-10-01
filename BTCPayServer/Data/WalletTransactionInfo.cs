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

        [Obsolete]
        Dictionary<string, LabelData>? _LegacyLabels;
        [JsonIgnore]
        [Obsolete]
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
                                PayoutLabel legacyPayoutLabel;
                                if (legacyLabels.TryGetValue(tag.Label, out var existing) &&
                                    existing is PayoutLabel)
                                {
                                    legacyPayoutLabel = (PayoutLabel)existing;
                                }
                                else
                                {
                                    legacyPayoutLabel = new PayoutLabel();
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
