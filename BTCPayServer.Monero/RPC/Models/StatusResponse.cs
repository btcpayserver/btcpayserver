using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public class StatusResponse
    {
        [JsonProperty("status")]             public string Status { get; set; }    
    }
}