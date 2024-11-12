using System;
using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class OnChainAutomatedPayoutSettings
{
    public string PayoutMethodId { get; set; }
    public int? FeeBlockTarget { get; set; }
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
    public decimal Threshold { get; set; }
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
    public bool  ProcessNewPayoutsInstantly { get; set; }
}
