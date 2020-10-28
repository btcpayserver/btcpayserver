using System;

namespace BTCPayServer.Client.Models
{
    public class WebhookSubscription
    {
        public string EventType { get; set; }
        public Uri Url { get; set; }

        public override string ToString()
        {
            return $"{Url}_{EventType}";
        }
    }
}
