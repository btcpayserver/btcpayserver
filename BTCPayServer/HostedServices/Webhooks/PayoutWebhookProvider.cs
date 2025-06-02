using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.Extensions.Logging;
using WebhookDeliveryData = BTCPayServer.Data.WebhookDeliveryData;

namespace BTCPayServer.HostedServices.Webhooks;

public class PayoutWebhookProvider(
    EventAggregator eventAggregator,
    ILogger<PayoutWebhookProvider> logger,
    WebhookSender webhookSender,
    BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings)
    : WebhookProvider<PayoutEvent>(eventAggregator, logger, webhookSender)
{
    protected override WebhookSender.WebhookDeliveryRequest CreateDeliveryRequest(PayoutEvent payoutEvent, WebhookData webhook)
    {
        WebhookBlob webhookBlob = webhook?.GetBlob();

        WebhookPayoutEvent webhookEvent = GetWebhookEvent(payoutEvent)!;
        webhookEvent.StoreId = payoutEvent.Payout.StoreDataId;
        webhookEvent.PayoutId = payoutEvent.Payout.Id;
        webhookEvent.PayoutState = payoutEvent.Payout.State;
        webhookEvent.PullPaymentId = payoutEvent.Payout.PullPaymentDataId;
        webhookEvent.WebhookId = webhook?.Id;
        webhookEvent.IsRedelivery = false;
        WebhookDeliveryData delivery = webhook is null ? null : WebhookExtensions.NewWebhookDelivery(webhook.Id);
        if (delivery is not null)
        {
            webhookEvent.DeliveryId = delivery.Id;
            webhookEvent.OriginalDeliveryId = delivery.Id;
            webhookEvent.Timestamp = delivery.Timestamp;
        }

        return new PayoutWebhookDeliveryRequest(payoutEvent, webhook?.Id, webhookEvent, delivery, webhookBlob, btcPayNetworkJsonSerializerSettings);
    }

    public override Dictionary<string, string> GetSupportedWebhookTypes()
    {
        return new Dictionary<string, string>
        {
            { WebhookEventType.PayoutCreated, "Payout - Created" },
            { WebhookEventType.PayoutApproved, "Payout - Approved" },
            { WebhookEventType.PayoutUpdated, "Payout - Updated" }
        };
    }

    public override WebhookEvent CreateTestEvent(string type, object[] args)
    {
        string storeId = args[0].ToString();
        return new WebhookPayoutEvent(type, storeId) { PayoutId = "__test__" + Guid.NewGuid() + "__test__" };
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
