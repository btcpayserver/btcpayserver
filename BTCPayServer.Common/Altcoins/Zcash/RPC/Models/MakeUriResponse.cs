using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Zcash.RPC.Models
{
    public partial class MakeUriResponse
    {
        [JsonProperty("uri")] public string Uri { get; set; }
    }
}
