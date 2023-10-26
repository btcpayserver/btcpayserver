using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Chia.RPC.Models
{
    public partial class GetWalletsRequest
    {
        [JsonProperty("type")] public int Type { get; set; }
    }
}
