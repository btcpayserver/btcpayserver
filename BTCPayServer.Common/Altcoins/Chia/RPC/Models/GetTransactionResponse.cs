using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Chia.RPC.Models
{
    public partial class GetTransactionResponse
    {
        [JsonProperty("transaction")] public TransactionRecord Transaction { get; set; }

        public record TransactionRecord
        {
            [JsonProperty("amount")] public ulong Amount { get; init; }
            [JsonProperty("confirmed_at_height")] public int ConfirmedAtHeight { get; init; }

            [JsonIgnore] public string TransactionId => Name;

            [JsonProperty("name")] public string Name { get; init; } = string.Empty;

            [JsonProperty("to_address")] public string ToAddress { get; init; } = string.Empty;
        }
    }
}
