using System;
using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Shopify.ApiModels
{
    public class CreateScriptResponse
    {
        [JsonProperty("script_tag")]
        public ScriptTag ScriptTag { get; set; }
    }
    
    public class ScriptTag    {
        [JsonProperty("id")]
        public int Id { get; set; } 

        [JsonProperty("src")]
        public string Src { get; set; } 

        [JsonProperty("event")]
        public string Event { get; set; } 

        [JsonProperty("created_at")]
        public DateTime CreatedAt { get; set; } 

        [JsonProperty("updated_at")]
        public DateTime UpdatedAt { get; set; } 

        [JsonProperty("display_scope")]
        public string DisplayScope { get; set; } 
    }
}
