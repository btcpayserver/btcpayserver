using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class CreatePlanRequest
{
    public string Description { get; set; }
    public string Currency { get; set; }
    public int? GracePeriodDays { get; set; }
    public string Name { get; set; }
    public bool? OptimisticActivation { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? Price { get; set; }
    public bool? Renewable { get; set; }
    public int? TrialDays { get; set; }
    public JObject Metadata { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public OfferingPlanModel.RecurringInterval? RecurringType { get; set; }

    public string[] Features { get; set; }
}
