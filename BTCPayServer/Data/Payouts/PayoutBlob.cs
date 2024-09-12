using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public class PayoutBlob
    {
        public int MinimumConfirmation { get; set; } = 1;
        public string Destination { get; set; }
        public int Revision { get; set; }
        
        [JsonExtensionData]
        public Dictionary<string, JToken> AdditionalData { get; set; } = new();

        public JObject Metadata { get; set; }
    }
}
