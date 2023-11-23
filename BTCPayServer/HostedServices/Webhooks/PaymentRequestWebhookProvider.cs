﻿using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services.PaymentRequests;
using Microsoft.Extensions.Logging;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class PaymentRequestWebhookProvider: WebhookProvider<PaymentRequestEvent>
{
    public PaymentRequestWebhookProvider(EventAggregator eventAggregator, ILogger<PaymentRequestWebhookProvider> logger, WebhookSender webhookSender) : base(eventAggregator, logger, webhookSender)
    {
    }

    public override Dictionary<string, string> GetSupportedWebhookTypes()
    {
        return new Dictionary<string, string>()
        {
            {WebhookEventType.PaymentRequestCreated, "Payment Request Created"},
            {WebhookEventType.PaymentRequestUpdated, "Payment Request Updated"},
            {WebhookEventType.PaymentRequestArchived, "Payment Request Archived"},
            {WebhookEventType.PaymentRequestStatusChanged, "Payment Request Status Changed"},
        };
    }

    public override WebhookEvent CreateTestEvent(string type, object[] args)
    {
        return new WebhookPayoutEvent(type)
        {
            StoreId = args[0].ToString(),
            PayoutId = "__test__" + Guid.NewGuid() + "__test__"
        };
    }

    protected override WebhookPaymentRequestEvent GetWebhookEvent(PaymentRequestEvent evt)
    {
        return evt.Type switch
        {
            PaymentRequestEvent.Created => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestCreated),
            PaymentRequestEvent.Updated => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestUpdated),
            PaymentRequestEvent.Archived => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestArchived),
            PaymentRequestEvent.StatusChanged => new WebhookPaymentRequestEvent(WebhookEventType.PaymentRequestStatusChanged),
            _ => null
        };
    }

    public override WebhookSender.WebhookDeliveryRequest CreateDeliveryRequest(PaymentRequestEvent paymentRequestEvent, WebhookData webhook)
    {
        var webhookBlob = webhook?.GetBlob();
        var webhookEvent = GetWebhookEvent(paymentRequestEvent)!;
        webhookEvent.StoreId = paymentRequestEvent.Data.StoreDataId;
        webhookEvent.PaymentRequestId = paymentRequestEvent.Data.Id;
        webhookEvent.Status = paymentRequestEvent.Data.Status;
        webhookEvent.WebhookId = webhook?.Id;
        webhookEvent.IsRedelivery = false;
        WebhookDeliveryData delivery = webhook is null? null: WebhookExtensions.NewWebhookDelivery(webhook.Id);
        if (delivery is not null)
        {
            webhookEvent.DeliveryId = delivery.Id;
            webhookEvent.OriginalDeliveryId = delivery.Id;
            webhookEvent.Timestamp = delivery.Timestamp;
        }
        return new PaymentRequestWebhookDeliveryRequest(paymentRequestEvent,webhook?.Id, webhookEvent, delivery, webhookBlob );
    }
}
