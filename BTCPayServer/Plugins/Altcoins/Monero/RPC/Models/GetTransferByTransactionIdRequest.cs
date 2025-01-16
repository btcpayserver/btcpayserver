using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Monero.RPC.Models
{
    public class GetTransferByTransactionIdRequest
    {
        [JsonProperty("txid")] public string TransactionId { get; set; }

        [JsonProperty("account_index", DefaultValueHandling = DefaultValueHandling.Ignore)] public long? AccountIndex { get; set; }
    }
}
