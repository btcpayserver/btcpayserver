using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Monero.RPC.Models
{
    public partial class GetTransfersResponse
    {
        [JsonProperty("in")] public List<GetTransfersResponseItem> In { get; set; }
        [JsonProperty("out")] public List<GetTransfersResponseItem> Out { get; set; }
        [JsonProperty("pending")] public List<GetTransfersResponseItem> Pending { get; set; }
        [JsonProperty("failed")] public List<GetTransfersResponseItem> Failed { get; set; }
        [JsonProperty("pool")] public List<GetTransfersResponseItem> Pool { get; set; }

        public partial class GetTransfersResponseItem

        {
            [JsonProperty("address")] public string Address { get; set; }
            [JsonProperty("amount")] public long Amount { get; set; }
            [JsonProperty("confirmations")] public long Confirmations { get; set; }
            [JsonProperty("double_spend_seen")] public bool DoubleSpendSeen { get; set; }
            [JsonProperty("height")] public long Height { get; set; }
            [JsonProperty("note")] public string Note { get; set; }
            [JsonProperty("payment_id")] public string PaymentId { get; set; }
            [JsonProperty("subaddr_index")] public SubaddrIndex SubaddrIndex { get; set; }

            [JsonProperty("suggested_confirmations_threshold")]
            public long SuggestedConfirmationsThreshold { get; set; }

            [JsonProperty("timestamp")] public long Timestamp { get; set; }
            [JsonProperty("txid")] public string Txid { get; set; }
            [JsonProperty("type")] public string Type { get; set; }
            [JsonProperty("unlock_time")] public long UnlockTime { get; set; }
        }
    }
}
