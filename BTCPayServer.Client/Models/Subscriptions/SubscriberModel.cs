using System;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class SubscriberModel
{
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset Created { get; set; }
    public CustomerModel Customer { get; set; }
    public OfferingModel Offering { get; set; }
    public OfferingPlanModel Plan { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? PeriodEnd { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? TrialEnd { get; set; }

    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset? GracePeriodEnd { get; set; }

    public bool IsActive { get; set; }
    public bool IsSuspended { get; set; }
    public string SuspensionReason { get; set; }
    public bool AutoRenew { get; set; }
    public JObject Metadata { get; set; }
    public string ProcessingInvoiceId { get; set; }
    public OfferingPlanModel NextPlan { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public SubscriptionPhase Phase { get; set; }
}
