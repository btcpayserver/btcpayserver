using Newtonsoft.Json;

namespace BTCPayServer.Common.Altcoins.Chia.RPC.Models
{
    public partial class GetTransactionsRequest
    {
        [JsonProperty("wallet_id")] public int WalletId { get; set; }
    }
}
