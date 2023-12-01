using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Monero.RPC.Models
{
    public partial class CreateAccountRequest
    {
        [JsonProperty("label")] public string Label { get; set; }
    }
}
