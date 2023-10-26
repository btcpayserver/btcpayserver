using Newtonsoft.Json;

namespace BTCPayServer.Common.Altcoins.Chia.RPC.Models
{
    public partial class GetHeightInfoResponse
    {
        [JsonProperty("height")] public long Height { get; set; }
    }
}
