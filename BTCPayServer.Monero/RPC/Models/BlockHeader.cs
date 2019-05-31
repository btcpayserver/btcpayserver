using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public partial class BlockHeader
    {
        [JsonProperty("block_size")]    public long BlockSize { get; set; }   
        [JsonProperty("depth")]         public long Depth { get; set; }       
        [JsonProperty("difficulty")]    public long Difficulty { get; set; }  
        [JsonProperty("hash")]          public string Hash { get; set; }      
        [JsonProperty("height")]        public long Height { get; set; }      
        [JsonProperty("major_version")] public long MajorVersion { get; set; }
        [JsonProperty("minor_version")] public long MinorVersion { get; set; }
        [JsonProperty("nonce")]         public long Nonce { get; set; }       
        [JsonProperty("num_txes")]      public long NumTxes { get; set; }     
        [JsonProperty("orphan_status")] public bool OrphanStatus { get; set; }
        [JsonProperty("prev_hash")]     public string PrevHash { get; set; }  
        [JsonProperty("reward")]        public long Reward { get; set; }      
        [JsonProperty("timestamp")]     public long Timestamp { get; set; }   
    }
}