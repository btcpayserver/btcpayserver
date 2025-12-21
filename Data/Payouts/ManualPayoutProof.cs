using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public class ManualPayoutProof : IPayoutProof
    {
        public static string Type = "ManualPayoutProof";
        public string ProofType { get; } = Type;
        public string Link { get; set; }
        public string Id { get; set; }

        [JsonExtensionData] public Dictionary<string, JToken> AdditionalData { get; set; }
    }
}
