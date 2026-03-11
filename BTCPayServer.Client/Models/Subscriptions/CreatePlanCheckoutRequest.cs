using System;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public class CreatePlanCheckoutRequest
{
    public string StoreId { get; set; }
    public string OfferingId { get; set; }
    public string PlanId { get; set; }
    public string CustomerSelector { get; set; }
    [JsonConverter(typeof(JsonConverters.TimeSpanJsonConverter.Minutes))]
    public TimeSpan? DurationMinutes { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public OnPayBehavior? OnPayBehavior { get; set; }

    public JObject NewSubscriberMetadata { get; set; }
    public JObject InvoiceMetadata { get; set; }
    public JObject Metadata { get; set; }
    public bool? IsTrial { get; set; }
    /// <summary>
    /// The amount of credit to purchase. The amount of credit purchased will be equal to what need to be paid
    /// to top-up the plan.
    /// If enough credit is available, the plan will start immediately.
    /// </summary>
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? CreditPurchase { get; set; }
    public string SuccessRedirectLink { get; set; }
    public string NewSubscriberEmail { get; set; }
}
