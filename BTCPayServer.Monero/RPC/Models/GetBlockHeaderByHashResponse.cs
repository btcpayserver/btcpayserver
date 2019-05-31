using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public partial class GetBlockHeaderByHashResponse
    {
        [JsonProperty("block_header")] public BlockHeader BlockHeader { get; set; }
        [JsonProperty("status")]       public string Status { get; set; }          
        [JsonProperty("untrusted")]    public bool Untrusted { get; set; }         
    }
}