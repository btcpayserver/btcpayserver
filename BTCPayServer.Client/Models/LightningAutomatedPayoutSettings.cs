using System;
using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class LightningAutomatedPayoutSettings
{
    public string PayoutMethodId { get; set; }

    [JsonConverter(typeof(TimeSpanJsonConverter.Seconds))]
    public TimeSpan IntervalSeconds { get; set; }

    public int? CancelPayoutAfterFailures { get; set; }
    [JsonProperty(DefaultValueHandling = DefaultValueHandling.Populate)]
    public bool  ProcessNewPayoutsInstantly { get; set; }
    
}
