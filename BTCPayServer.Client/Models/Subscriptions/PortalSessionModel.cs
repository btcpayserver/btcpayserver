using System;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class PortalSessionModel
{
    public string BaseUrl { get; set; }
    public string Id { get; set; }
    public SubscriberModel Subscriber { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? Expiration { get; set; }
    public bool IsExpired { get; set; }
    public string Url { get; set; }
}
