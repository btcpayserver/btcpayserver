using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Shopify.Models;

namespace BTCPayServer.Models.StoreViewModels
{
    public class IntegrationsViewModel
    {
        public ShopifySettings Shopify { get; set; }
        public string EventPublicKey { get; set; }
        public List<WebhookSubscription> Webhooks { get; set; } = new List<WebhookSubscription>();
    }
}
