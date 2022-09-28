using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class LabelData
    {
        public string Type { get; set; }
        public string Text { get; set; }

        [JsonIgnore]
        public virtual string TaintId => string.Empty;

        [JsonExtensionData] public Dictionary<string, JToken> AdditionalData { get; set; }
    }
}
