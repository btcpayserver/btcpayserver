using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models;

public enum AppItemPriceType
{
    Fixed,
    Topup,
    Minimum
}

public class AppItem
{
    public string Id { get; set; }
    public string Title { get; set; }
    public bool Disabled { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string Description { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string[] Categories { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string Image { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public AppItemPriceType PriceType { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? Price { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string BuyButtonText { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public int? Inventory { get; set; }

    [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
    public string[] PaymentMethods { get; set; }

    [JsonExtensionData]
    public Dictionary<string, object> AdditionalData { get; set; }
}
