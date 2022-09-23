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
using BTCPayServer.HostedServices;
using NBitcoin;

namespace BTCPayServer.Services.Labels
{
    public class LabelFactory
    {
        private readonly LinkGenerator _linkGenerator;
        private readonly  WalletRepository _walletRepository;

        public LabelFactory(
            LinkGenerator linkGenerator,
            WalletRepository walletRepository
        )
        {
            _linkGenerator = linkGenerator;
            _walletRepository = walletRepository;
        }

        public IEnumerable<ColoredLabel> ColorizeTransactionLabels(List<WalletLabelData> labels,
            HttpRequest request)
        {
            foreach (var label in labels)
            {
                var parsedLabel = label.GetLabel();
                yield return CreateLabel(parsedLabel, request);
            }
        }

        const string DefaultColor = "#000";
        private ColoredLabel CreateLabel(LabelData label, HttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(label);

            ColoredLabel coloredLabel = new ColoredLabel
            {
                Text = label.Text,
                Color = string.IsNullOrEmpty(label.Color)? DefaultColor: label.Color,
                Tooltip = "",
                TextColor = TextColor(label.Color)
            };

            string PayoutLabelText(KeyValuePair<string, List<string>>? pair = null)
            {
                if (pair is null)
                {
                    return "Paid a payout";
                }
                return pair.Value.Value.Count == 1 ? $"Paid a payout {(string.IsNullOrEmpty(pair.Value.Key)? string.Empty: $"of a pull payment ({pair.Value.Key})")}" : $"Paid {pair.Value.Value.Count} payouts {(string.IsNullOrEmpty(pair.Value.Key)? string.Empty: $"of a pull payment ({pair.Value.Key})")}";
            }

            if (label is Label.ReferenceLabel refLabel)
            {
                var refInLabel = string.IsNullOrEmpty(refLabel.Reference) ? string.Empty : $"({refLabel.Reference})";
                switch (label.Type)
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
            else if (label is Label.PayoutLabel payoutLabel)
            {
                coloredLabel.Tooltip = payoutLabel.PullPaymentPayouts?.Count switch
                {
                    null => PayoutLabelText(),
                    0 => PayoutLabelText(),
                    1 => PayoutLabelText(payoutLabel.PullPaymentPayouts.First()),
                    _ =>
                        $"<ul>{string.Join(string.Empty, payoutLabel.PullPaymentPayouts.Select(pair => $"<li>{PayoutLabelText(pair)}</li>"))}</ul>"
                };

                coloredLabel.Link = string.IsNullOrEmpty(payoutLabel.WalletId)
                    ? null
                    : _linkGenerator.PayoutLink(payoutLabel.WalletId, null, PayoutState.Completed, request.Scheme, request.Host,
                        request.PathBase);
            }
            else if (label.Text == "payjoin")
            {
                coloredLabel.Tooltip = $"This UTXO was part of a PayJoin transaction.";
            }
            return coloredLabel;
        }

        // Borrowed from https://github.com/ManageIQ/guides/blob/master/labels.md
        readonly string[] LabelColorScheme =
        {
            "#fbca04",
            "#0e8a16",
            "#ff7619",
            "#84b6eb",
            "#5319e7",
            "#cdcdcd",
            "#cc317c",
        };

        readonly int MaxLabelSize = 20;

        async public Task<Label.RawLabel> BuildLabel(
            WalletBlobInfo walletBlobInfo,
            HttpRequest request,
            WalletId walletId,
            string label,
            string color
        )
        {

            color ??= LabelColorScheme.ElementAt(new Random().Next(0, LabelColorScheme.Length - 1));
            label = label.Trim().TrimStart('{').ToLowerInvariant().Replace(',', ' ').Truncate(MaxLabelSize);
            return new Label.RawLabel(label, color);
        }

        private string TextColor(string bgColor)
        {
            int nThreshold = 105;
            var bg = ColorTranslator.FromHtml(bgColor);
            int bgDelta = Convert.ToInt32((bg.R * 0.299) + (bg.G * 0.587) +  (bg.B * 0.114));
            Color color = (255 - bgDelta < nThreshold) ? Color.Black : Color.White;
            return ColorTranslator.ToHtml(color);
        }
    }
}
