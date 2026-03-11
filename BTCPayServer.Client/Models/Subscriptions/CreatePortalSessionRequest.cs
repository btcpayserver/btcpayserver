using System;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class CreatePortalSessionRequest
{
    public string StoreId { get; set; }
    public string OfferingId { get; set; }
    public string CustomerSelector { get; set; }
    [JsonConverter(typeof(JsonConverters.TimeSpanJsonConverter.Minutes))]
    public TimeSpan? DurationMinutes { get; set; }
}
