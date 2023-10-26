using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Common.Altcoins.Chia.RPC.Models
{
    public partial class GetWalletsResponse
    {
        [JsonProperty("wallets")] public List<WalletEntry> Wallets { get; set; }

        public partial class WalletEntry
        {
            [JsonProperty("id")] public int Id { get; set; }
            [JsonProperty("name")] public string Name { get; set; }
            [JsonProperty("type")] public int Type { get; set; }
        }
    }
}
