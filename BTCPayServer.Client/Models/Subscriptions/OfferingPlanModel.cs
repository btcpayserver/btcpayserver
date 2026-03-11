using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class OfferingPlanModel
{
    [JsonConverter(typeof(StringEnumConverter))]
    public enum PlanStatus
    {
        Active,
        Retired
    }

    [JsonConverter(typeof(StringEnumConverter))]
    public enum RecurringInterval
    {
        Monthly,
        Quarterly,
        Yearly,
        Lifetime
    }

    public string Id { get; set; }
    public string Name { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public PlanStatus Status { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal Price { get; set; }
    public string Currency { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public RecurringInterval RecurringType { get; set; }
    public int GracePeriodDays { get; set; }
    public int TrialDays { get; set; }
    public string Description { get; set; }
    public int MemberCount { get; set; }
    public bool OptimisticActivation { get; set; }
    public string[] Features { get; set; }
    public bool Renewable { get; set; }
    public JObject Metadata { get; set; }
}
