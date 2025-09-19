using System;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class SubscriptionMemberModel
{
    public CustomerModel Customer { get; set; }
    public SubscriptionPlanModel Plan { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? PeriodEnd { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? TrialEnd { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? GracePeriodEnd { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? CanceledAt { get; set; }

    public bool IsActive { get; set; }

    public bool ForceDisabled { get; set; }
}
