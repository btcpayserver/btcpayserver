using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public class GetTransferByTransactionIdRequest
    {
        [JsonProperty("txid")] public string TransactionId { get; set; }

        [JsonProperty("account_index")] public long AccountIndex { get; set; }
    }
}