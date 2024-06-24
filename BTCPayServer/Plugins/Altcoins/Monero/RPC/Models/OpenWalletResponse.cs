using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Monero.RPC.Models
{
    public partial class OpenWalletResponse
    {
        [JsonProperty("id")] public string Id { get; set; }
        [JsonProperty("jsonrpc")] public string Jsonrpc { get; set; }
        [JsonProperty("result")] public object Result { get; set; }
        [JsonProperty("error")] public OpenWalletErrorResponse Error { get; set; }
    }
}
