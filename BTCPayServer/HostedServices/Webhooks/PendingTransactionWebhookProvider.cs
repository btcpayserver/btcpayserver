using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;

namespace BTCPayServer.HostedServices.Webhooks;

public class PendingTransactionWebhookProvider(
    WebhookSender webhookSender,
    EventAggregator eventAggregator,
    ILogger<InvoiceWebhookProvider> logger)
    : WebhookProvider<PendingTransactionService.PendingTransactionEvent>(eventAggregator, logger, webhookSender)
{
    public const string PendingTransactionCreated = nameof(PendingTransactionCreated);
    public const string PendingTransactionSignatureCollected = nameof(PendingTransactionSignatureCollected);
    public const string PendingTransactionBroadcast = nameof(PendingTransactionBroadcast);
    public const string PendingTransactionCancelled = nameof(PendingTransactionCancelled);

    public override bool SupportsCustomerEmail { get; } = false;

    public override Dictionary<string, string> GetSupportedWebhookTypes()
    {
        return new Dictionary<string, string>
        {
            { PendingTransactionCreated, "Pending Transaction - Created" },
            { PendingTransactionSignatureCollected, "Pending Transaction - Signature Collected" },
            { PendingTransactionBroadcast, "Pending Transaction - Broadcast" },
            { PendingTransactionCancelled, "Pending Transaction - Cancelled" }
        };
    }

    protected override WebhookSender.WebhookDeliveryRequest CreateDeliveryRequest(PendingTransactionService.PendingTransactionEvent evt,
        WebhookData webhook)
    {
        var webhookBlob = webhook?.GetBlob();

        var webhookEvent = GetWebhookEvent(evt)!;
        webhookEvent.StoreId = evt.Data.StoreId;
        webhookEvent.WebhookId = webhook?.Id;
        webhookEvent.IsRedelivery = false;
        var delivery = webhook is null ? null : WebhookExtensions.NewWebhookDelivery(webhook.Id);
        if (delivery is not null)
        {
            webhookEvent.DeliveryId = delivery.Id;
            webhookEvent.OriginalDeliveryId = delivery.Id;
            webhookEvent.Timestamp = delivery.Timestamp;
        }

        return new PendingTransactionDeliveryRequest(evt, webhook?.Id, webhookEvent, delivery, webhookBlob);
    }

    protected override WebhookPendingTransactionEvent GetWebhookEvent(PendingTransactionService.PendingTransactionEvent evt)
    {
        return evt.Type switch
        {
            PendingTransactionService.PendingTransactionEvent.Created => new WebhookPendingTransactionEvent(
                PendingTransactionCreated, evt.Data.StoreId),
            PendingTransactionService.PendingTransactionEvent.SignatureCollected => new WebhookPendingTransactionEvent(
                PendingTransactionSignatureCollected, evt.Data.StoreId),
            PendingTransactionService.PendingTransactionEvent.Broadcast => new WebhookPendingTransactionEvent(
                PendingTransactionBroadcast, evt.Data.StoreId),
            PendingTransactionService.PendingTransactionEvent.Cancelled => new WebhookPendingTransactionEvent(
                PendingTransactionCancelled, evt.Data.StoreId),
            _ => null
        };
    }

    public override WebhookEvent CreateTestEvent(string type, params object[] args)
    {
        var storeId = args[0].ToString();
        return new WebhookPendingTransactionEvent(type, storeId) { PendingTransactionId = "__test__" + Guid.NewGuid() + "__test__" };
    }


    public class WebhookPendingTransactionEvent : StoreWebhookEvent
    {
        public WebhookPendingTransactionEvent(string type, string storeId)
        {
            if (!type.StartsWith(PendingTransactionCreated.Replace("Created", "").ToLower(), StringComparison.InvariantCultureIgnoreCase))
                throw new ArgumentException("Invalid event type", nameof(type));
            Type = type;
            StoreId = storeId;
        }

        [JsonProperty(Order = 2)] public string PendingTransactionId { get; set; }
    }
}
