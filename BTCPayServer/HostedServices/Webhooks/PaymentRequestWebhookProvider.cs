#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices.Webhooks;

public class PaymentRequestWebhookProvider : EventHostedServiceBase, IWebhookProvider
{
    private readonly WebhookSender _webhookSender;

    public PaymentRequestWebhookProvider(WebhookSender webhookSender, 
        EventAggregator eventAggregator, 
        ILogger<PaymentRequestWebhookProvider> logger) : base(
        eventAggregator, logger)
    {
        _webhookSender = webhookSender;
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<PaymentRequestEvent>();
        base.SubscribeToEvents();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is PaymentRequestEvent paymentRequestEvent)
        {
            var webhooks = await _webhookSender.GetWebhooks(paymentRequestEvent.Data.StoreDataId);
            foreach (var webhook in webhooks)
            {
                var webhookBlob = webhook.GetBlob();
                if (GetWebhookEvent(paymentRequestEvent) is not { } webhookEvent)
                    continue;
                if (!webhookBlob.ShouldDeliver(webhookEvent.Type))
                    continue;

                Data.WebhookDeliveryData delivery = WebhookExtensions.NewWebhookDelivery(webhook.Id);
                webhookEvent.StoreId = paymentRequestEvent.Data.StoreDataId;
                webhookEvent.PaymentRequestId = paymentRequestEvent.Data.Id;
                webhookEvent.Status = paymentRequestEvent.Data.Status;
                webhookEvent.DeliveryId = delivery.Id;
                webhookEvent.WebhookId = webhook.Id;
                webhookEvent.OriginalDeliveryId = delivery.Id;
                webhookEvent.IsRedelivery = false;
                webhookEvent.Timestamp = delivery.Timestamp;
                var context = new WebhookSender.WebhookDeliveryRequest(webhook.Id, webhookEvent, delivery, webhookBlob);
                _webhookSender.EnqueueDelivery(context);
            }
        }

        await base.ProcessEvent(evt, cancellationToken);
    }

    public Dictionary<string, string> GetSupportedWebhookTypes()
    {
        return new Dictionary<string, string>()
        {
            {WebhookEventType.PaymentRequestCreated, "Payment Request Created"},
            {WebhookEventType.PaymentRequestUpdated, "Payment Request Updated"},
            {WebhookEventType.PaymentRequestArchived, "Payment Request Archived"},
            {WebhookEventType.PaymentRequestStatusChanged, "Payment Request Status Changed"},
        };
    }

    public WebhookEvent CreateTestEvent(string type, object[] args)
    {
        return new WebhookPayoutEvent(type)
        {
            StoreId = args[0].ToString(),
            PayoutId = "__test__" + Guid.NewGuid() + "__test__"
        };
    }

    private static WebhookPaymentRequestEvent? GetWebhookEvent(PaymentRequestEvent paymentRequestEvent)
    {
        return paymentRequestEvent.Type switch
        {
            PaymentRequestEvent.Created => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestCreated),
            PaymentRequestEvent.Updated => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestUpdated),
            PaymentRequestEvent.Archived => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestArchived),
            PaymentRequestEvent.StatusChanged => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestStatusChanged),
            _ => null
        };
    }
}
