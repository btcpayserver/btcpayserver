using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public partial class GetHeightResponse
    {
        [JsonProperty("height")] public long Height { get; set; }
    }
}