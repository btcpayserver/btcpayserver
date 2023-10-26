using Newtonsoft.Json;

namespace BTCPayServer.Services.Altcoins.Chia.RPC.Models
{
    public partial class GetNextAddressResponse
    {
        [JsonProperty("address")] public string Address { get; set; }
    }
}
