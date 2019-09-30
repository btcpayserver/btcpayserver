using Newtonsoft.Json;

namespace BTCPayServer.Shitcoins.Monero.RPC.Models
{
    public partial class Peer
    {
        [JsonProperty("info")] public Info Info { get; set; }
    }
}