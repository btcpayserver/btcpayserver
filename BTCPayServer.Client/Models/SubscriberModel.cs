using System;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class SubscriberModel
{
    public CustomerModel Customer { get; set; }
    public OfferingModel Offer { get; set; }
    public SubscriptionPlanModel Plan { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? PeriodEnd { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? TrialEnd { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? GracePeriodEnd { get; set; }

    public bool IsActive { get; set; }
    public bool IsSuspended { get; set; }
    public string SuspensionReason { get; set; }
}
