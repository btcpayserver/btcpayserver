using System;
using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Shopify.ApiModels
{
    public class CreateWebhookResponse
    {
        [JsonProperty("webhook")] public Webhook Webhook { get; set; }
    }

    public class Webhook
    {
        [JsonProperty("id")] public int Id { get; set; }

        [JsonProperty("address")] public string Address { get; set; }

        [JsonProperty("topic")] public string Topic { get; set; }

        [JsonProperty("created_at")] public DateTime CreatedAt { get; set; }

        [JsonProperty("updated_at")] public DateTime UpdatedAt { get; set; }

        [JsonProperty("format")] public string Format { get; set; }

        [JsonProperty("fields")] public List<object> Fields { get; set; }

        [JsonProperty("metafield_namespaces")] public List<object> MetafieldNamespaces { get; set; }

        [JsonProperty("api_version")] public string ApiVersion { get; set; }

        [JsonProperty("private_metafield_namespaces")]
        public List<object> PrivateMetafieldNamespaces { get; set; }
    }
}
