using System;
using System.Collections.Generic;
using System.Text;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class StoreWebhookBaseData
    {
        public class AuthorizedEventsData
        {
            public bool Everything { get; set; } = true;

            public string[] SpecificEvents { get; set; } = Array.Empty<string>();
        }

        public bool Enabled { get; set; } = true;
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string Secret { get; set; }
        public bool AutomaticRedelivery { get; set; } = true;
        public string Url { get; set; }
        public AuthorizedEventsData AuthorizedEvents { get; set; } = new AuthorizedEventsData();
    }
    public class UpdateStoreWebhookRequest : StoreWebhookBaseData
    {
    }
    public class CreateStoreWebhookRequest : StoreWebhookBaseData
    {
    }
    public class StoreWebhookData : StoreWebhookBaseData
    {
        public string Id { get; set; }
    }
}
