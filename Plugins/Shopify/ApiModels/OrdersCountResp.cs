using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Shopify.ApiModels
{
    public class CountResponse
    {
        [JsonProperty("count")]
        public long Count { get; set; }
    }
}
