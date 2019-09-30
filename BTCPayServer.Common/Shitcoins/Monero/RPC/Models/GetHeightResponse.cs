using Newtonsoft.Json;

namespace BTCPayServer.Shitcoins.Monero.RPC.Models
{
    public partial class GetHeightResponse
    {
        [JsonProperty("height")] public long Height { get; set; }
    }
}