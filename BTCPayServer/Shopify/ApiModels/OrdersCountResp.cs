using Newtonsoft.Json;

namespace BTCPayServer.Services.Shopify.ApiModels
{
    public class CountResponse
    {
        [JsonProperty("count")] 
        public long Count { get; set; }
    }
}
