﻿using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices.Webhooks;

public class PayoutWebhookProvider : WebhookProvider<PayoutEvent>
{
    private readonly BTCPayNetworkJsonSerializerSettings _btcPayNetworkJsonSerializerSettings;

    public PayoutWebhookProvider(EventAggregator eventAggregator, ILogger<PayoutWebhookProvider> logger, WebhookSender webhookSender, BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings) : base(eventAggregator, logger, webhookSender)
    {
        _btcPayNetworkJsonSerializerSettings = btcPayNetworkJsonSerializerSettings;
    }
    
    public override WebhookSender.WebhookDeliveryRequest CreateDeliveryRequest(PayoutEvent payoutEvent, WebhookData? webhook)
    {
        var webhookBlob = webhook?.GetBlob();

        var webhookEvent = GetWebhookEvent(payoutEvent)!;
        webhookEvent.StoreId = payoutEvent.Payout.StoreDataId;
        webhookEvent.PayoutId = payoutEvent.Payout.Id;
        webhookEvent.PayoutState = payoutEvent.Payout.State;
        webhookEvent.PullPaymentId = payoutEvent.Payout.PullPaymentDataId;
        webhookEvent.WebhookId = webhook?.Id;
        webhookEvent.IsRedelivery = false;
        Data.WebhookDeliveryData? delivery = webhook is null? null:  WebhookExtensions.NewWebhookDelivery(webhook.Id);
        if (delivery is not null)
        {
            webhookEvent.DeliveryId = delivery.Id;
            webhookEvent.OriginalDeliveryId = delivery.Id;
            webhookEvent.Timestamp = delivery.Timestamp;
        }
        return new PayoutWebhookDeliveryRequest(payoutEvent,webhook?.Id, webhookEvent, delivery, webhookBlob, _btcPayNetworkJsonSerializerSettings);
    }

    public override Dictionary<string, string> GetSupportedWebhookTypes()
    {
        return new Dictionary<string, string>()
        {
            {WebhookEventType.PayoutCreated, "A payout has been created"},
            {WebhookEventType.PayoutApproved, "A payout has been approved"},
            {WebhookEventType.PayoutUpdated, "A payout was updated"}
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

    protected override WebhookPayoutEvent? GetWebhookEvent(PayoutEvent payoutEvent)
    {
        return payoutEvent.Type switch
        {
            PayoutEvent.PayoutEventType.Created => new WebhookPayoutEvent(WebhookEventType.PayoutCreated),
            PayoutEvent.PayoutEventType.Approved => new WebhookPayoutEvent(WebhookEventType.PayoutCreated),
            PayoutEvent.PayoutEventType.Updated => new WebhookPayoutEvent(WebhookEventType.PayoutCreated),
            _ => null
        };
    }
}
