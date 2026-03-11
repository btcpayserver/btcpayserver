using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models;

// Subscription phases carried by subscriber-related webhook events
[JsonConverter(typeof(StringEnumConverter))]
public enum SubscriptionPhase
{
    Normal,
    Expired,
    Grace,
    Trial
}
