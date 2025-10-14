using System;

namespace BTCPayServer.Plugins.Webhooks.Views
{
    public class WebhooksViewModel
    {
        public class WebhookViewModel
        {
            public string Id { get; set; }
            public string Url { get; set; }
            public bool LastDeliverySuccessful { get; set; } = true;
            public DateTimeOffset? LastDeliveryTimeStamp { get; set; } = null;
            public string LastDeliveryErrorMessage { get; set; } = null;
        }
        public WebhookViewModel[] Webhooks { get; set; }
    }
}
