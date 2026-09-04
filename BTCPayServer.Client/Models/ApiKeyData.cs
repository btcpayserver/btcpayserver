using System;
using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class ApiKeyData
    {
        public string Id { get; set; }
        public string ApiKey { get; set; }
        public string Label { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? Created { get; set; }

        [JsonProperty(ItemConverterType = typeof(PermissionJsonConverter))]
        public Permission[] Permissions { get; set; }
    }
}
