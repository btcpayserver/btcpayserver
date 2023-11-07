using Newtonsoft.Json;

namespace BTCPayServer.Common.Altcoins.Chia.RPC.Models
{
    public partial class GetTransactionsRequest
    {
        [JsonProperty("wallet_id")] public int WalletId { get; set; }
        [JsonProperty("sort_key")] public string SortKey { get; set; }
        [JsonProperty("reverse")] public bool Reverse { get; set; }
    }
}
