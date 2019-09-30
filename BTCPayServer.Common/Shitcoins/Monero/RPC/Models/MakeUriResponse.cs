using Newtonsoft.Json;

namespace BTCPayServer.Shitcoins.Monero.RPC.Models
{
    public partial class MakeUriResponse
    {
        [JsonProperty("uri")] public string Uri { get; set; }
    }
}