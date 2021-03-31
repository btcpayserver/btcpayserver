using System;
using System.Collections.Generic;
using Amazon.Util.Internal.PlatformServices;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Labels
{
    public class LabelFactory
    {
        private readonly LinkGenerator _linkGenerator;

        public LabelFactory(LinkGenerator linkGenerator)
        {
            _linkGenerator = linkGenerator;
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
        private ColoredLabel CreateLabel(LabelData uncoloredLabel, string color, HttpRequest request)
        {
            if (uncoloredLabel == null)
                throw new ArgumentNullException(nameof(uncoloredLabel));
            color = color ?? DefaultColor;

            ColoredLabel coloredLabel = new ColoredLabel()
            {
                Text = uncoloredLabel.Text,
                Color = color
            };
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
                        coloredLabel.Tooltip = $"This utxo was exposed through a payjoin proposal for an invoice {refInLabel}";
                        coloredLabel.Link = string.IsNullOrEmpty(refLabel.Reference)
                            ? null
                            : _linkGenerator.InvoiceLink(refLabel.Reference, request.Scheme, request.Host, request.PathBase);
                        break;
                }
            }
            else if (uncoloredLabel is PayoutLabel payoutLabel)
            {
                coloredLabel.Tooltip = $"Paid a payout of a pull payment ({payoutLabel.PullPaymentId})";
                coloredLabel.Link = string.IsNullOrEmpty(payoutLabel.PullPaymentId) || string.IsNullOrEmpty(payoutLabel.WalletId)
                    ? null
                    : _linkGenerator.PayoutLink(payoutLabel.WalletId,
                        payoutLabel.PullPaymentId, request.Scheme, request.Host,
                        request.PathBase);
            }
            return coloredLabel;
        }
    }
}
