using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class OfferingModel
{
    public string Id { get; set; } = null!;
    public string StoreId { get; set; }
    public string AppName { get; set; }
    public string AppId { get; set; } = null!;
    public string SuccessRedirectUrl { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public List<OfferingPlanModel> Plans { get; set; }
    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public List<FeatureModel> Features { get; set; }
    public JObject Metadata { get; set; }
}
