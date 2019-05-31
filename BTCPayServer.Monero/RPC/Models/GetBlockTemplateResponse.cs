using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public partial class GetBlockTemplateResponse
    {
        [JsonProperty("blockhashing_blob")]  public string BlockhashingBlob { get; set; } 
        [JsonProperty("blocktemplate_blob")] public string BlocktemplateBlob { get; set; }
        [JsonProperty("difficulty")]         public long Difficulty { get; set; }         
        [JsonProperty("expected_reward")]    public long ExpectedReward { get; set; }     
        [JsonProperty("height")]             public long Height { get; set; }             
        [JsonProperty("prev_hash")]          public string PrevHash { get; set; }         
        [JsonProperty("reserved_offset")]    public long ReservedOffset { get; set; }     
        [JsonProperty("status")]             public string Status { get; set; }           
        [JsonProperty("untrusted")]          public bool Untrusted { get; set; }          
    }
}