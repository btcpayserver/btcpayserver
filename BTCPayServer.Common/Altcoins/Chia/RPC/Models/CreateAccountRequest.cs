using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Chia.RPC.Models
{
    public partial class CreateAccountRequest
    {
        [JsonProperty("label")] public string Label { get; set; }
    }
}
