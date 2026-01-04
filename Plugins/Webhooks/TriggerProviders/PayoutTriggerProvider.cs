#nullable enable
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Services;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins.Webhooks.TriggerProviders;

public class PayoutTriggerProvider(BTCPayNetworkJsonSerializerSettings btcPayNetworkJsonSerializerSettings) : WebhookTriggerProvider<PayoutEvent>
{
    protected override async Task<JObject> GetEmailModel(WebhookTriggerContext<PayoutEvent> webhookTriggerContext)
    {
        var evt = webhookTriggerContext.Event;
        var blob = evt.Payout.GetBlob(btcPayNetworkJsonSerializerSettings);
        var model = await base.GetEmailModel(webhookTriggerContext);
        model["Payout"] = new JObject()
        {
            ["Id"] = evt.Payout.Id,
            ["PullPaymentId"] = evt.Payout.PullPaymentDataId,
            ["Destination"] = evt.Payout.DedupId ?? blob.Destination,
            ["State"] = evt.Payout.State.ToString(),
            ["Metadata"] = blob.Metadata
        };
        return model;
    }
    protected override StoreWebhookEvent? GetWebhookEvent(PayoutEvent payoutEvent)
    {
        var webhookEvt = payoutEvent.Type switch
        {
            PayoutEvent.PayoutEventType.Created => new WebhookPayoutEvent(WebhookEventType.PayoutCreated, payoutEvent.Payout.StoreDataId),
            PayoutEvent.PayoutEventType.Approved => new WebhookPayoutEvent(WebhookEventType.PayoutApproved, payoutEvent.Payout.StoreDataId),
            PayoutEvent.PayoutEventType.Updated => new WebhookPayoutEvent(WebhookEventType.PayoutUpdated, payoutEvent.Payout.StoreDataId),
            _ => null
        };
        if (webhookEvt is null)
            return null;
        webhookEvt.PayoutId = payoutEvent.Payout.Id;
        webhookEvt.PayoutState = payoutEvent.Payout.State;
        webhookEvt.PullPaymentId = payoutEvent.Payout.PullPaymentDataId;
        return webhookEvt;
    }
}
