using System.Collections.Generic;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class PermissionMetadata
    {
        [JsonProperty("name")]
        public string PermissionName { get; set; }
        [JsonProperty("included")]
        public List<string> SubPermissions { get; set; } = new List<string>();
    }
}
