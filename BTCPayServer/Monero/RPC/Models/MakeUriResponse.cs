using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public partial class MakeUriResponse
    {
        [JsonProperty("uri")] public string Uri { get; set; }
    }
}