using System;
using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class LightningAutomatedPayoutSettings
{
    public string PayoutMethodId { get; set; }

    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
    public bool  ProcessNewPayoutsInstantly { get; set; }
    
}
