using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Common.Altcoins.Chia.RPC.Models
{
    public class GetFeeEstimateRequest
    {
        [JsonProperty("spend_type")] public string SpendType { get; set; }
        [JsonProperty("target_times")] public List<int> TargetTimes { get; set; }
    }
}
