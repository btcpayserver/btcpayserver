#nullable enable
using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices.Webhooks;

public class PayoutWebhookProvider : EventHostedServiceBase, IWebhookProvider
{
    private readonly WebhookSender _webhookSender;

    public PayoutWebhookProvider(WebhookSender webhookSender, EventAggregator eventAggregator, ILogger<PayoutWebhookProvider> logger) : base(
        eventAggregator, logger)
    {
        _webhookSender = webhookSender;
    }

    protected override void SubscribeToEvents()
    {
        Subscribe<PayoutEvent>();
        base.SubscribeToEvents();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is PayoutEvent payoutEvent)
        {
            var webhooks = await _webhookSender.GetWebhooks(payoutEvent.Payout.StoreDataId);
            foreach (var webhook in webhooks)
            {
                var webhookBlob = webhook.GetBlob();
                if (GetWebhookEvent(payoutEvent) is not { } webhookEvent)
                    continue;
                if (!webhookBlob.ShouldDeliver(webhookEvent.Type))
                    continue;

                Data.WebhookDeliveryData delivery = WebhookExtensions.NewWebhookDelivery(webhook.Id);
                webhookEvent.StoreId = payoutEvent.Payout.StoreDataId;
                webhookEvent.PayoutId = payoutEvent.Payout.Id;
                webhookEvent.PayoutState = payoutEvent.Payout.State;
                webhookEvent.PullPaymentId = payoutEvent.Payout.PullPaymentDataId;
                webhookEvent.DeliveryId = delivery.Id;
                webhookEvent.WebhookId = webhook.Id;
                webhookEvent.OriginalDeliveryId = delivery.Id;
                webhookEvent.IsRedelivery = false;
                webhookEvent.Timestamp = delivery.Timestamp;
                var context = new PayoutWebhookDeliveryRequest(payoutEvent,webhook.Id, webhookEvent, delivery, webhookBlob);
                _webhookSender.EnqueueDelivery(context);
            }
        }

        await base.ProcessEvent(evt, cancellationToken);
    }

    public Dictionary<string, string> GetSupportedWebhookTypes()
    {
        return new Dictionary<string, string>()
        {
            {WebhookEventType.PayoutCreated, "A payout has been created"},
            {WebhookEventType.PayoutApproved, "A payout has been approved"},
            {WebhookEventType.PayoutUpdated, "A payout was updated"}
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

    private static WebhookPayoutEvent? GetWebhookEvent(PayoutEvent payoutEvent)
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
