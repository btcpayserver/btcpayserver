using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Client.Models;
using BTCPayServer.Data;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.HostedServices.Webhooks;

public abstract class WebhookProvider<T>: EventHostedServiceBase, IWebhookProvider
{
    private readonly WebhookSender _webhookSender;
    public abstract Dictionary<string, string> GetSupportedWebhookTypes();

    public abstract WebhookSender.WebhookDeliveryRequest CreateDeliveryRequest(T evt, WebhookData? webhook);

    public abstract WebhookEvent CreateTestEvent(string type, params object[] args);

    protected WebhookProvider(EventAggregator eventAggregator, ILogger logger, WebhookSender webhookSender) : base(eventAggregator, logger)
    {
        _webhookSender = webhookSender;
    }
    
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
            if (GetWebhookEvent(tEvt) is not { } webhookEventX )
                return;
            
            
            var webhooks = await _webhookSender.GetWebhooks(webhookEventX.StoreId, webhookEventX.Type );
            
            foreach (var webhook in webhooks)
            {
                _webhookSender.EnqueueDelivery(CreateDeliveryRequest(tEvt, webhook));
            }

            EventAggregator.Publish(CreateDeliveryRequest(tEvt, null));
        }

        await base.ProcessEvent(evt, cancellationToken);
    }
    
}
