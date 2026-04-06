using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class FeatureModel
{
    public string Id { get; set; }
    public string Description  { get; set; }
    [JsonExtensionData]
    public Dictionary<string, JToken> AdditionalData { get; set; }
}
