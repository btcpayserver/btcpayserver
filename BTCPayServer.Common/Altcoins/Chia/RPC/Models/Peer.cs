using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Chia.RPC.Models
{
    public partial class Peer
    {
        [JsonProperty("info")] public Info Info { get; set; }
    }
}
