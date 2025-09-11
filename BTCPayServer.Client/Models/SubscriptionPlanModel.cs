using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class SubscriptionPlanModel
{
    public class PlanItemModel
    {
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Quantity { get; set; }
        public string Id { get; set; }
    }
    public string Id { get; set; }
    public List<PlanItemModel> Items { get; set; }
}
