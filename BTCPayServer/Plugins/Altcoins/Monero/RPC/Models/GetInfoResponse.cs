using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Monero.RPC.Models
{
    public partial class GetInfoResponse
    {
        [JsonProperty("height")] public long Height { get; set; }
        [JsonProperty("busy_syncing")] public bool BusySyncing { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("target_height")] public long? TargetHeight { get; set; }
    }
}
