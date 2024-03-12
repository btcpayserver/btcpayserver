using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Zcash.RPC.Models
{
    public partial class GetHeightResponse
    {
        [JsonProperty("height")] public long Height { get; set; }
    }
}
