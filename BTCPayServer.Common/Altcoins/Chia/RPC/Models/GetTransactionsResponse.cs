using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Chia.RPC.Models
{
    public partial class GetTransactionsResponse
    {
        [JsonProperty("transactions")] public List<GetTransactionResponse.TransactionRecord> In { get; set; }
    }
}
