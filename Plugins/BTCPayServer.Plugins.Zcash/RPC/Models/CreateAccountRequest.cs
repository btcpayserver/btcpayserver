using Newtonsoft.Json;

namespace BTCPayServer.Plugins.Zcash.RPC.Models
{
    public partial class CreateAccountRequest
    {
        [JsonProperty("label")] public string Label { get; set; }
    }
}
