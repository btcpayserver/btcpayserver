using Newtonsoft.Json;

namespace BTCPayServer.Monero.RPC.Models
{
    public partial class CreateAccountRequest
    {
        [JsonProperty("label")] public string Label { get; set; }
    }
}