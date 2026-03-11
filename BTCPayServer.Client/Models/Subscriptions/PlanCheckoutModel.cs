using System;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

[JsonConverter(typeof(StringEnumConverter))]
public enum OnPayBehavior
{
    /// <summary>
    /// Starts the plan if payment is due, else, do not and add the funds to the credit.
    /// </summary>
    SoftMigration,
    /// <summary>
    /// Starts the plan immediately. If a payment wasn't due yet, reimburse the unused part of the period,
    /// and start the plan.
    /// </summary>
    HardMigration
}
public class PlanCheckoutModel
{
    public SubscriberModel Subscriber { get; set; }
    public OfferingPlanModel Plan { get; set; }
    public string BaseUrl { get; set; }
    public string Id { get; set; }
    public string InvoiceId { get; set; }
    public string SuccessRedirectUrl { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset Expiration { get; set; }
    public string RedirectUrl { get; set; }
    public JObject InvoiceMetadata { get; set; }
    public JObject Metadata { get; set; }
    public bool NewSubscriber { get; set; }
    public bool IsTrial { get; set; }
    [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
    public DateTimeOffset Created { get; set; }
    public bool PlanStarted { get; set; }
    public JObject NewSubscriberMetadata { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? RefundAmount { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal CreditedByInvoice { get; set; }
    [JsonConverter(typeof(StringEnumConverter))]
    public OnPayBehavior OnPayBehavior { get; set; }
    public bool IsExpired { get; set; }
    public string Url { get; set; }

    /// <summary>
    /// The amount of credit to purchase. The amount of credit purchased will be equal to what need to be paid
    /// to top-up the plan.
    /// If enough credit is available, the plan will start immediately.
    /// </summary>
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? CreditPurchase { get; set; }
}
