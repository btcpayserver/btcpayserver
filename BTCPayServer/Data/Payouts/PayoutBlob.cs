using System;
using System.Collections.Generic;
using System.Linq;
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
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public string[] DisabledProcessors { get; set; }
        [JsonExtensionData]
        public Dictionary<string, JToken> AdditionalData { get; set; } = new();

        public JObject Metadata { get; set; }

        public void DisableProcessor(string processorName)
        {
            DisabledProcessors ??= Array.Empty<string>();
            DisabledProcessors = DisabledProcessors.Concat(new[] { processorName }).ToArray();
        }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public int? ErrorCount { get; set; }
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public bool NonInteractiveOnly { get; set; }
        public int IncrementErrorCount()
        {
            if (ErrorCount is { } c)
                ErrorCount = c + 1;
            else
                ErrorCount = 1;
            return ErrorCount.Value;
        }
    }
}
