using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Data
{
    public class ManualPayoutProof : IPayoutProof
    {
        public static string Type = "ManualPayoutProof";
        public string ProofType { get; } = Type;
        private string _link;
        public string Link
        {
            get => _link;
            set => _link = Uri.TryCreate(value, UriKind.Absolute, out var uri) && uri.Scheme is "http" or "https"
                ? uri.AbsoluteUri
                : null;
        }
        public string Id { get; set; }

        [JsonExtensionData] public Dictionary<string, JToken> AdditionalData { get; set; }
    }
}
