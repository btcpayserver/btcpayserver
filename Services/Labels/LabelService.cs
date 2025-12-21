#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Models.WalletViewModels;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using NBitcoin;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Services.Labels;

public class LabelService
{
    private readonly LinkGenerator _linkGenerator;

    public LabelService(LinkGenerator linkGenerator)
    {
        _linkGenerator = linkGenerator;
    }

    public IEnumerable<TransactionTagModel> CreateTransactionTagModels(WalletTransactionInfo? transactionInfo, HttpRequest req)
    {
        if (transactionInfo is null)
            return Array.Empty<TransactionTagModel>();

        string PayoutTooltip(IGrouping<string, string>? payoutsByPullPaymentId = null)
        {
            if (payoutsByPullPaymentId is null)
            {
                return "Paid a payout";
            }

            if (payoutsByPullPaymentId.Count() == 1)
            {
                var pp = payoutsByPullPaymentId.Key;
                var payout = payoutsByPullPaymentId.First();
                return !string.IsNullOrEmpty(pp)
                    ? $"Paid a payout ({payout}) of a pull payment ({pp})"
                    : $"Paid a payout {payout}";
            }
            else
            {
                var pp = payoutsByPullPaymentId.Key;
                return !string.IsNullOrEmpty(pp)
                    ? $"Paid {payoutsByPullPaymentId.Count()} payouts of a pull payment ({pp})"
                    : $"Paid {payoutsByPullPaymentId.Count()} payouts";
            }
        }

        var models = new Dictionary<string, TransactionTagModel>();
        foreach (var tag in transactionInfo.Attachments)
        {
            if (models.ContainsKey(tag.Type))
                continue;
            if (!transactionInfo.LabelColors.TryGetValue(tag.Type, out var color))
                continue;
            var model = new TransactionTagModel
            {
                Text = tag.Type,
                Color = color,
                TextColor = ColorPalette.Default.TextColor(color)
            };
            models.Add(tag.Type, model);
            if (tag.Type == WalletObjectData.Types.Payout)
            {
                var payoutsByPullPaymentId =
                    transactionInfo.Attachments.Where(t => t.Type == "payout")
                    .GroupBy(t => t.Data?["pullPaymentId"]?.Value<string>() ?? "",
                             k => k.Id).ToList();

                model.Tooltip = payoutsByPullPaymentId.Count switch
                {
                    0 => PayoutTooltip(),
                    1 => PayoutTooltip(payoutsByPullPaymentId.First()),
                    _ => string.Join(", ", payoutsByPullPaymentId.Select(PayoutTooltip))
                };

                model.Link = _linkGenerator.PayoutLink(transactionInfo.WalletId.ToString(), null,
                    PayoutState.Completed, req.Scheme, req.Host, req.PathBase);
            }
            else if (tag.Type == WalletObjectData.Types.Payjoin)
            {
                model.Tooltip = "This UTXO was part of a PayJoin transaction.";
            }
            else if (tag.Type == WalletObjectData.Types.Invoice)
            {
                model.Tooltip = $"Received through an invoice {tag.Id}";
                model.Link = string.IsNullOrEmpty(tag.Id)
                        ? null
                        : _linkGenerator.InvoiceLink(tag.Id, req.Scheme, req.Host, req.PathBase);
            }
            else if (tag.Type == WalletObjectData.Types.RBF)
            {
                var txs = ((tag.LinkData?["txs"] as JArray)?.Select(e => e.ToString()) ?? []).ToHashSet();
                var txsStr = string.Join(", ", txs);
                model.Tooltip = $"This is transaction is replacing the following transactions: {txsStr}";
                model.Link = "#";
            }
            else if (tag.Type == WalletObjectData.Types.CPFP)
            {
                var txs = ((tag.LinkData?["outpoints"] as JArray)?.Select(e => OutPoint.Parse(e.ToString()).Hash) ?? []).ToHashSet();
                var txsStr = string.Join(", ", txs);
                model.Tooltip = $"This is transaction is paying for fee for the following transactions: {txsStr}";
                model.Link = "#";
            }
            else if (tag.Type == WalletObjectData.Types.PaymentRequest)
            {
                var title = tag.Data?["title"]?.ToString() ?? tag.Id;
                model.Tooltip = $"Payment request: {title}";
                model.Link = _linkGenerator.PaymentRequestLink(tag.Id, req.Scheme, req.Host, req.PathBase);
            }
            else if (tag.Type == WalletObjectData.Types.App)
            {
                model.Tooltip = $"Received through an app {tag.Id}";
                model.Link = _linkGenerator.AppLink(tag.Id, req.Scheme, req.Host, req.PathBase);
            }
            else if (tag.Type == WalletObjectData.Types.PayjoinExposed)
            {

                if (tag.Id.Length != 0)
                {
                    model.Tooltip = $"This UTXO was exposed through a PayJoin proposal for an invoice ({tag.Id})";
                    model.Link = _linkGenerator.InvoiceLink(tag.Id, req.Scheme, req.Host, req.PathBase);
                }
                else
                {
                    model.Tooltip = $"This UTXO was exposed through a PayJoin proposal";
                }
            }
            else if (tag.Type == WalletObjectData.Types.PullPayment)
            {
                model.Tooltip = $"Received through a pull payment {tag.Id}";
                model.Link = _linkGenerator.PullPaymentLink(tag.Id, req.Scheme, req.Host, req.PathBase);
            }
            else if (tag.Type == WalletObjectData.Types.Payjoin)
            {
                model.Tooltip = $"This UTXO was part of a PayJoin transaction.";
            }
            else
            {
                model.Tooltip = tag.Data?.TryGetValue("tooltip", StringComparison.InvariantCultureIgnoreCase, out var tooltip) is true ? tooltip.ToString() : tag.Id;
                if (tag.Data?.TryGetValue("link", StringComparison.InvariantCultureIgnoreCase, out var link) is true)
                {
                    model.Link = link.ToString();
                }
            }
        }
        foreach (var label in transactionInfo.LabelColors)
            models.TryAdd(label.Key, new TransactionTagModel
            {
                Text = label.Key,
                Color = label.Value,
                TextColor = ColorPalette.Default.TextColor(label.Value)
            });
        return models.Values.OrderBy(v => v.Text);
    }
}
