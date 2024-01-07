using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices.Webhooks;

public abstract class WebhookProvider<T>(EventAggregator eventAggregator, ILogger logger, WebhookSender webhookSender)
    : EventHostedServiceBase(eventAggregator, logger), IWebhookProvider
{
    public abstract Dictionary<string, string> GetSupportedWebhookTypes();

    protected abstract WebhookSender.WebhookDeliveryRequest CreateDeliveryRequest(T evt, WebhookData webhook);

    public abstract WebhookEvent CreateTestEvent(string type, params object[] args);

    protected abstract StoreWebhookEvent GetWebhookEvent(T evt);
    
    protected override void SubscribeToEvents()
    {
        Subscribe<T>();
        base.SubscribeToEvents();
    }
    protected override async Task ProcessEvent(object evt, CancellationToken cancellationToken)
    {
        if (evt is T tEvt)
        {
            if (GetWebhookEvent(tEvt) is not { } webhookEvent)
                return;

            var webhooks = await webhookSender.GetWebhooks(webhookEvent.StoreId, webhookEvent.Type);
            foreach (var webhook in webhooks)
            {
                webhookSender.EnqueueDelivery(CreateDeliveryRequest(tEvt, webhook));
            }

            EventAggregator.Publish(CreateDeliveryRequest(tEvt, null));
        }

        await base.ProcessEvent(evt, cancellationToken);
    }
    
}
