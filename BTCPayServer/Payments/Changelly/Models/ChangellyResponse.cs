using Newtonsoft.Json;

namespace BTCPayServer.Payments.Changelly.Models
{
    
    public class ChangellyResponse<T>
    {
        [JsonProperty("jsonrpc")]
        public string JsonRPC { get; set; }
        [JsonProperty("id")]
        public object Id { get; set; }
        [JsonProperty("result")]
        public T Result { get; set; }
        [JsonProperty("error")]
        public Error Error { get; set; }
    }
}
