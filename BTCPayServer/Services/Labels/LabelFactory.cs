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
        private readonly  WalletRepository _walletRepository;

        public LabelFactory(
            LinkGenerator linkGenerator,
            WalletRepository walletRepository
        )
        {
            _linkGenerator = linkGenerator;
            _walletRepository = walletRepository;
        }

        public IEnumerable<ColoredLabel> ColorizeTransactionLabels(WalletBlobInfo walletBlobInfo, WalletTransactionInfo transactionInfo,
            HttpRequest request)
        {
            foreach (var label in transactionInfo.Labels)
            {
                walletBlobInfo.LabelColors.TryGetValue(label.Value.Text, out var color);
                yield return CreateLabel(label.Value, color, request);
            }
        }

        public IEnumerable<ColoredLabel> GetWalletColoredLabels(WalletBlobInfo walletBlobInfo, HttpRequest request)
        {
            foreach (var kv in walletBlobInfo.LabelColors)
            {
                yield return CreateLabel(new RawLabel() { Text = kv.Key }, kv.Value, request);
            }
        }

        const string DefaultColor = "#000";
        private ColoredLabel CreateLabel(LabelData uncoloredLabel, string? color, HttpRequest request)
        {
            ArgumentNullException.ThrowIfNull(uncoloredLabel);
            color ??= DefaultColor;

            ColoredLabel coloredLabel = new ColoredLabel
            {
                Text = uncoloredLabel.Text,
                Color = color,
                Tooltip = "",
                TextColor = TextColor(color)
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
            else if (uncoloredLabel is PayoutLabel payoutLabel)
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
            else if (uncoloredLabel.Text == "payjoin")
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

        async public Task<RawLabel> BuildLabel(
            WalletBlobInfo walletBlobInfo,
            HttpRequest request,
            WalletTransactionInfo walletTransactionInfo,
            WalletId walletId,
            string transactionId,
            string label
        )
        {
            label = label.Trim().TrimStart('{').ToLowerInvariant().Replace(',', ' ').Truncate(MaxLabelSize);
            var labels = GetWalletColoredLabels(walletBlobInfo, request);

            if (!labels.Any(l => l.Text.Equals(label, StringComparison.OrdinalIgnoreCase)))
            {
                var chosenColor = ChooseBackgroundColor(walletBlobInfo, request);
                walletBlobInfo.LabelColors.Add(label, chosenColor);
                await _walletRepository.SetWalletInfo(walletId, walletBlobInfo);
            }

            return new RawLabel(label);
        }

        private string ChooseBackgroundColor(
            WalletBlobInfo walletBlobInfo,
            HttpRequest request
        )
        {
            var labels = GetWalletColoredLabels(walletBlobInfo, request);

            List<string> allColors = new List<string>();
            allColors.AddRange(LabelColorScheme);
            allColors.AddRange(labels.Select(l => l.Color));
            var chosenColor =
                allColors
                .GroupBy(k => k)
                .OrderBy(k => k.Count())
                .ThenBy(k =>
                {
                    var indexInColorScheme = Array.IndexOf(LabelColorScheme, k.Key);

                    // Ensures that any label color which may not be in our label color scheme is given the least priority
                    return indexInColorScheme == -1 ? double.PositiveInfinity : indexInColorScheme;
                })
                .First().Key;

            return chosenColor;
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
