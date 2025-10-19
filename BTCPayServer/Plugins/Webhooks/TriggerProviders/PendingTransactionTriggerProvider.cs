#nullable  enable
using System.Threading.Tasks;
using BTCPayServer.Abstractions;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Routing;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Webhooks.TriggerProviders;

public class PendingTransactionTriggerProvider
    (LinkGenerator linkGenerator) : WebhookTriggerProvider<PendingTransactionService.PendingTransactionEvent>
{
    public const string PendingTransactionCreated = nameof(PendingTransactionCreated);
    public const string PendingTransactionSignatureCollected = nameof(PendingTransactionSignatureCollected);
    public const string PendingTransactionBroadcast = nameof(PendingTransactionBroadcast);
    public const string PendingTransactionCancelled = nameof(PendingTransactionCancelled);

    protected override async Task<JObject> GetEmailModel(WebhookTriggerContext<PendingTransactionService.PendingTransactionEvent> webhookTriggerContext)
    {
        var evt = webhookTriggerContext.Event;
        var id = evt.Data.TransactionId;
        var trimmedId = !string.IsNullOrEmpty(id) && id.Length > 15 ? $"{id.Substring(0, 7)}...{id.Substring(id.Length - 7)}" : id;

        var blob = evt.Data.GetBlob() ?? new();
        var model = await base.GetEmailModel(webhookTriggerContext);
        var o = new JObject()
        {
            ["Id"] = id,
            ["TrimmedId"] = trimmedId,
            ["StoreId"] = evt.Data.StoreId,
            ["SignaturesCollected"] = blob.SignaturesCollected,
            ["SignaturesNeeded"] = blob.SignaturesNeeded,
            ["SignaturesTotal"] = blob.SignaturesTotal
        };
        model["PendingTransaction"] = o;
        if (blob.RequestBaseUrl is not null && RequestBaseUrl.TryFromUrl(blob.RequestBaseUrl, out var v))
        {
            o["Link"] = linkGenerator.WalletTransactionsLink(new(webhookTriggerContext.Store.Id, evt.Data.CryptoCode), v);
        }

        return model;
    }

    protected override WebhookPendingTransactionEvent? GetWebhookEvent(PendingTransactionService.PendingTransactionEvent evt)
    {
        var webhook = evt.Type switch
        {
            PendingTransactionService.PendingTransactionEvent.Created => new WebhookPendingTransactionEvent(
                PendingTransactionCreated, evt.Data.StoreId, evt.Data.CryptoCode),
            PendingTransactionService.PendingTransactionEvent.SignatureCollected => new WebhookPendingTransactionEvent(
                PendingTransactionSignatureCollected, evt.Data.StoreId, evt.Data.CryptoCode),
            PendingTransactionService.PendingTransactionEvent.Broadcast => new WebhookPendingTransactionEvent(
                PendingTransactionBroadcast, evt.Data.StoreId, evt.Data.CryptoCode),
            PendingTransactionService.PendingTransactionEvent.Cancelled => new WebhookPendingTransactionEvent(
                PendingTransactionCancelled, evt.Data.StoreId, evt.Data.CryptoCode),
            _ => null
        };
        if (webhook is not null)
            webhook.PendingTransactionId = evt.Data.TransactionId;
        return webhook;
    }

    public class WebhookPendingTransactionEvent : StoreWebhookEvent
    {
        public WebhookPendingTransactionEvent(string type, string storeId, string cryptoCode)
        {
            Type = type;
            StoreId = storeId;
            CryptoCode = cryptoCode;
        }

        public string CryptoCode { get; set; }

        [JsonProperty(Order = 2)] public string PendingTransactionId { get; set; } = null!;
    }
}
