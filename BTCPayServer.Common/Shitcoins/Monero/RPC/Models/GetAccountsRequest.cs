using Newtonsoft.Json;

namespace BTCPayServer.Shitcoins.Monero.RPC.Models
{
    public partial class GetAccountsRequest
    {
        [JsonProperty("tag")] public string Tag { get; set; }
    }
}