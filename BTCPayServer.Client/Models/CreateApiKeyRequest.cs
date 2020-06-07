using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class CreateApiKeyRequest
    {
        public string Label { get; set; }

        [JsonProperty(ItemConverterType = typeof(PermissionJsonConverter))]
        public Permission[] Permissions { get; set; }
    }
}
