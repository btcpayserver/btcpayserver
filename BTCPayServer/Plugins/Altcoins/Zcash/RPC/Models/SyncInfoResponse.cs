using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Zcash.RPC.Models
{
    public partial class SyncInfoResponse
    {
        [JsonProperty("height")] public long Height { get; set; }
        [JsonProperty("peers")] public List<Peer> Peers { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("target_height")] public long? TargetHeight { get; set; }
    }
}
