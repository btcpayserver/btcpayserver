#nullable enable
using System;
using System.Drawing;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using BTCPayServer.Data;
using BTCPayServer.Client.Models;
using BTCPayServer.Abstractions.Extensions;

namespace BTCPayServer.Services.Labels
{
    public class LabelFactory
    {
        private readonly LinkGenerator _linkGenerator;

        public LabelFactory(
            LinkGenerator linkGenerator
        )
        {
            _linkGenerator = linkGenerator;
        }

        public IEnumerable<ColoredLabel> ColorizeTransactionLabels(WalletTransactionInfo? transactionInfo,
            HttpRequest request)
        {
            if (transactionInfo?.LegacyLabels is null)
                yield break;
            foreach (var label in transactionInfo.LegacyLabels)
            {
                if (transactionInfo.LabelColors.TryGetValue(label.Key, out var color))
                    yield return CreateLabel(transactionInfo, label.Value, color, request);
            }
        }
        private ColoredLabel CreateLabel(WalletTransactionInfo transactionInfo, LabelData uncoloredLabel, string color, HttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(uncoloredLabel);

            ColoredLabel coloredLabel = new ColoredLabel
            {
                Text = uncoloredLabel.Text,
                Color = color,
                Tooltip = "",
                TextColor = ColorPalette.Default.TextColor(color)
            };

            string PayoutLabelText(KeyValuePair<string, List<string>>? pair = null)
            {
                if (pair is null)
                {
                    return "Paid a payout";
                }
                return pair.Value.Value.Count == 1 ? $"Paid a payout {(string.IsNullOrEmpty(pair.Value.Key)? string.Empty: $"of a pull payment ({pair.Value.Key})")}" : $"Paid {pair.Value.Value.Count} payouts {(string.IsNullOrEmpty(pair.Value.Key)? string.Empty: $"of a pull payment ({pair.Value.Key})")}";
            }

            if (uncoloredLabel is ReferenceLabel refLabel)
            {
                var refInLabel = string.IsNullOrEmpty(refLabel.Reference) ? string.Empty : $"({refLabel.Reference})";
                switch (uncoloredLabel.Type)
                {
                    case "invoice":
                        coloredLabel.Tooltip = $"Received through an invoice {refInLabel}";
                        coloredLabel.Link = string.IsNullOrEmpty(refLabel.Reference)
                                ? null
                                : _linkGenerator.InvoiceLink(refLabel.Reference, request.Scheme, request.Host, request.PathBase);
                        break;
                    case "payment-request":
                        coloredLabel.Tooltip = $"Received through a payment request {refInLabel}";
                        coloredLabel.Link = string.IsNullOrEmpty(refLabel.Reference)
                                ? null
                                : _linkGenerator.PaymentRequestLink(refLabel.Reference, request.Scheme, request.Host, request.PathBase);
                        break;
                    case "app":
                        coloredLabel.Tooltip = $"Received through an app {refInLabel}";
                        coloredLabel.Link = string.IsNullOrEmpty(refLabel.Reference)
                            ? null
                            : _linkGenerator.AppLink(refLabel.Reference, request.Scheme, request.Host, request.PathBase);
                        break;
                    case "pj-exposed":
                        coloredLabel.Tooltip = $"This UTXO was exposed through a PayJoin proposal for an invoice {refInLabel}";
                        coloredLabel.Link = string.IsNullOrEmpty(refLabel.Reference)
                            ? null
                            : _linkGenerator.InvoiceLink(refLabel.Reference, request.Scheme, request.Host, request.PathBase);
                        break;
                }
            }
            else if (uncoloredLabel is LegacyPayoutLabel payoutLabel)
            {
                coloredLabel.Tooltip = payoutLabel.PullPaymentPayouts?.Count switch
                {
                    null => PayoutLabelText(),
                    0 => PayoutLabelText(),
                    1 => PayoutLabelText(payoutLabel.PullPaymentPayouts.First()),
                    _ =>
                        $"<ul>{string.Join(string.Empty, payoutLabel.PullPaymentPayouts.Select(pair => $"<li>{PayoutLabelText(pair)}</li>"))}</ul>"
                };

                coloredLabel.Link = _linkGenerator.PayoutLink(transactionInfo.WalletId.ToString(), null, PayoutState.Completed, request.Scheme, request.Host,
                        request.PathBase);
            }
            else if (uncoloredLabel.Text == "payjoin")
            {
                coloredLabel.Tooltip = $"This UTXO was part of a PayJoin transaction.";
            }
            return coloredLabel;
        }
    }
}
