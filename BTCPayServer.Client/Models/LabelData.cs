using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    [Obsolete]
    public class LabelData
    {
        public string Type { get; set; }
        public string Text { get; set; }

        [JsonExtensionData] public Dictionary<string, JToken> AdditionalData { get; set; }
    }
}
