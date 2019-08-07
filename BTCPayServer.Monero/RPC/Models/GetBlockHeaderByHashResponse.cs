using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public partial class GetBlockHeaderByHashResponse
    {
        [JsonProperty("block_header")] public BlockHeader BlockHeader { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("untrusted")] public bool Untrusted { get; set; }
    }

    public class GetFeeEstimateRequest
    {
        [JsonProperty("grace_blocks")] public int? GraceBlocks { get; set; }
    }
    public class GetFeeEstimateResponse
    {
        [JsonProperty("fee")] public long Fee { get; set; }
        [JsonProperty("status")] public string Status { get; set; }
        [JsonProperty("untrusted")] public bool Untrusted { get; set; }
    }
    
}
