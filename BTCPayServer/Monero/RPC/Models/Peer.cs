using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public partial class Peer
    {
        [JsonProperty("info")] public Info Info { get; set; }
    }
}