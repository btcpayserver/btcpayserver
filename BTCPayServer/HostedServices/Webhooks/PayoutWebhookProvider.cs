using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices.Webhooks;

public class PayoutWebhookProvider(EventAggregator eventAggregator, ILogger<PayoutWebhookProvider> logger,
        WebhookSender webhookSender, BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings)
    : WebhookProvider<PayoutEvent>(eventAggregator, logger, webhookSender)
{
    protected override WebhookSender.WebhookDeliveryRequest CreateDeliveryRequest(PayoutEvent payoutEvent, WebhookData webhook)
    {
        var webhookBlob = webhook?.GetBlob();

        var webhookEvent = GetWebhookEvent(payoutEvent)!;
        webhookEvent.StoreId = payoutEvent.Payout.StoreDataId;
        webhookEvent.PayoutId = payoutEvent.Payout.Id;
        webhookEvent.PayoutState = payoutEvent.Payout.State;
        webhookEvent.PullPaymentId = payoutEvent.Payout.PullPaymentDataId;
        webhookEvent.WebhookId = webhook?.Id;
        webhookEvent.IsRedelivery = false;
        Data.WebhookDeliveryData delivery = webhook is null? null:  WebhookExtensions.NewWebhookDelivery(webhook.Id);
        if (delivery is not null)
        {
            webhookEvent.DeliveryId = delivery.Id;
            webhookEvent.OriginalDeliveryId = delivery.Id;
            webhookEvent.Timestamp = delivery.Timestamp;
        }
        return new PayoutWebhookDeliveryRequest(payoutEvent,webhook?.Id, webhookEvent, delivery, webhookBlob, btcPayNetworkJsonSerializerSettings);
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
        var storeId = args[0].ToString();
        return new WebhookPayoutEvent(type, storeId)
        {
            PayoutId = "__test__" + Guid.NewGuid() + "__test__"
        };
    }

    protected override WebhookPayoutEvent GetWebhookEvent(PayoutEvent payoutEvent)
    {
        return payoutEvent.Type switch
        {
            PayoutEvent.PayoutEventType.Created => new WebhookPayoutEvent(WebhookEventType.PayoutCreated, payoutEvent.Payout.StoreDataId),
            PayoutEvent.PayoutEventType.Approved => new WebhookPayoutEvent(WebhookEventType.PayoutApproved, payoutEvent.Payout.StoreDataId),
            PayoutEvent.PayoutEventType.Updated => new WebhookPayoutEvent(WebhookEventType.PayoutUpdated, payoutEvent.Payout.StoreDataId),
            _ => null
        };
    }
}
