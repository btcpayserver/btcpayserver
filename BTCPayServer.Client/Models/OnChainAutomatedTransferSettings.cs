using System;
using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class OnChainAutomatedTransferSettings
{
    public string PaymentMethod { get; set; }
       
    [JsonConverter(typeof(TimeSpanJsonConverter.Seconds))]
    public TimeSpan IntervalSeconds { get; set; }
}