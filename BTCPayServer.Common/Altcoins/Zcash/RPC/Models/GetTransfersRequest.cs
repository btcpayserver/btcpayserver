using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Zcash.RPC.Models
{
    public partial class GetTransfersRequest
    {
        [JsonProperty("in")] public bool In { get; set; }
        [JsonProperty("out")] public bool Out { get; set; }
        [JsonProperty("pending")] public bool Pending { get; set; }
        [JsonProperty("failed")] public bool Failed { get; set; }
        [JsonProperty("pool")] public bool Pool { get; set; }
        [JsonProperty("filter_by_height ")] public bool FilterByHeight { get; set; }
        [JsonProperty("min_height")] public long MinHeight { get; set; }
        [JsonProperty("max_height")] public long MaxHeight { get; set; }
        [JsonProperty("account_index")] public long AccountIndex { get; set; }
        [JsonProperty("subaddr_indices")] public List<long> SubaddrIndices { get; set; }
    }
}
