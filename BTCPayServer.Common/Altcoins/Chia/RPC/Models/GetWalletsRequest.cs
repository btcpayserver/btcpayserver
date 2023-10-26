using Newtonsoft.Json;

namespace BTCPayServer.Common.Altcoins.Chia.RPC.Models
{
    public partial class GetWalletsRequest
    {
        [JsonProperty("type")] public int Type { get; set; }
    }
}
