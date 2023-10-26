using Newtonsoft.Json;

namespace BTCPayServer.Common.Altcoins.Chia.RPC.Models
{
    public class GetTransactionRequest
    {
        [JsonProperty("transaction_id")] public string TransactionId { get; set; }
    }
}
