using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public partial class GetBlockCountResponse
    {
        [JsonProperty("count")]  public long Count { get; set; }   
        [JsonProperty("status")] public string Status { get; set; }
    }
}