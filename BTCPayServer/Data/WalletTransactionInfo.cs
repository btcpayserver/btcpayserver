#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Services;
using BTCPayServer.Services.Labels;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

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
        public List<Attachment> Attachments { get; set; } = new List<Attachment>();

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
                    foreach (var tag in Attachments)
                    {
                        switch (tag.Type)
                        {
                            case "payout":
                                PayoutLabel legacyPayoutLabel;
                                if (legacyLabels.TryGetValue(tag.Type, out var existing) &&
                                    existing is PayoutLabel)
                                {
                                    legacyPayoutLabel = (PayoutLabel)existing;
                                }
                                else
                                {
                                    legacyPayoutLabel = new PayoutLabel();
                                    legacyLabels.Add(tag.Type, legacyPayoutLabel);
                                }
                                var ppid = tag.Data?["pullPaymentId"]?.Value<string>() ?? "";
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
                                legacyLabels.TryAdd(tag.Type, new ReferenceLabel(tag.Type, tag.Id));
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

        public WalletTransactionInfo Merge(WalletTransactionInfo? value)
        {
            var result = new WalletTransactionInfo(WalletId);
            if (value is null)
                return result;

            if (result.WalletId != value.WalletId)
            {
                return result;
            }

            result.LabelColors = new Dictionary<string, string>(LabelColors);
            result.Attachments = new List<Attachment>(Attachments);
            foreach (var valueLabelColor in value.LabelColors)
            {
                result.LabelColors.TryAdd(valueLabelColor.Key, valueLabelColor.Value);
            }

            foreach (var valueAttachment in value.Attachments.Where(valueAttachment => !Attachments.Any(attachment =>
                         attachment.Id == valueAttachment.Id && attachment.Type == valueAttachment.Type)))
            {
                result.Attachments.Add(valueAttachment);
            }

            return result;
        }
    }
}
