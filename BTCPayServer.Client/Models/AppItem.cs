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
    public string Description { get; set; }
    public string[] Categories { get; set; }
    public string Image { get; set; }

    [JsonConverter(typeof(StringEnumConverter))]
    public AppItemPriceType PriceType { get; set; }

    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? Price { get; set; }
    public string BuyButtonText { get; set; }
    public int? Inventory { get; set; }

    [JsonExtensionData]
    public Dictionary<string, JToken> AdditionalData { get; set; }
}
