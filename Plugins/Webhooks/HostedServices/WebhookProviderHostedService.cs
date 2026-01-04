#nullable  enable
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using BTCPayServer.HostedServices;
using BTCPayServer.Plugins.Emails;
using BTCPayServer.Plugins.Emails.HostedServices;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
using StoreData = BTCPayServer.Data.StoreData;

namespace BTCPayServer.Plugins.Webhooks.HostedServices;

/// <summary>
/// This class listen to <typeparamref name="T"/> events and create webhook notifications and trigger event from it.
/// </summary>
public class WebhookProviderHostedService(
    EventAggregator eventAggregator,
    ApplicationDbContextFactory dbContextFactory,
    IEnumerable<WebhookTriggerProvider> webhookTriggerProviders,
    WebhookSender webhookSender,
    ILogger<WebhookProviderHostedService> logger)
    : EventHostedServiceBase(eventAggregator, logger)
{
    class WebhookTriggerOwner(WebhookTriggerProvider provider, WebhookTriggerContext ctx) : ITriggerOwner
    {
        public Task BeforeSending(EmailRuleMatchContext context)
        => provider.BeforeSending(context, ctx);
    }

    protected override void SubscribeToEvents()
    {
        SubscribeAny<object>();
    }

    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        var (provider, webhookEvent) = await GetWebhookEvent(evt);
        if (webhookEvent is null || provider is null)
            return;

        var webhooks = await webhookSender.GetWebhooks(webhookEvent.StoreId, webhookEvent.Type);
        foreach (var webhook in webhooks)
        {
            var ev = Clone(webhookEvent);
            ev.WebhookId = webhook.Id;
            var delivery = Data.WebhookDeliveryData.Create(webhook.Id);
            ev.DeliveryId = delivery.Id;
            ev.OriginalDeliveryId = delivery.Id;
            ev.Timestamp = delivery.Timestamp;
            ev.IsRedelivery = false;
            webhookSender.EnqueueDelivery(new(webhook.Id, ev, delivery, webhook.GetBlob()));
        }

        if (await GetStore(webhookEvent) is {} store)
        {
            var ctx = provider.CreateWebhookTriggerContext(store, evt, webhookEvent);
            var triggerEvent = new TriggerEvent(webhookEvent.StoreId, EmailRuleData.GetWebhookTriggerName(webhookEvent.Type),
                await provider.GetEmailModel(ctx), new WebhookTriggerOwner(provider, ctx));
            EventAggregator.Publish(triggerEvent);
        }
    }

    private async Task<(WebhookTriggerProvider?, StoreWebhookEvent?)> GetWebhookEvent(object evt)
    {
        foreach (var provider in webhookTriggerProviders)
        {
            var webhookEvent = await provider.GetWebhookEventAsync(evt);
            if (webhookEvent is not null)
                return (provider, webhookEvent);
        }
        return (null, null);
    }

    private StoreWebhookEvent Clone(StoreWebhookEvent webhookEvent)
    => (StoreWebhookEvent)JsonConvert.DeserializeObject(JsonConvert.SerializeObject(webhookEvent), webhookEvent.GetType(), WebhookSender.DefaultSerializerSettings)!;

    private async Task<StoreData?> GetStore(StoreWebhookEvent webhookEvent)
    {
        await using var ctx = dbContextFactory.CreateContext();
        return await ctx.Stores.FindAsync(webhookEvent.StoreId);
    }
}
