using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

public class CreatePosInvoiceRequest
{
    public string AppId { get; set; }
    public List<AppCartItem> Cart { get; set; }
    [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
    public List<decimal> Amounts { get; set; }
    public int? DiscountPercent { get; set; }
    public int? TipPercent { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? DiscountAmount { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? Tip { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? Subtotal { get; set; }
    [JsonConverter(typeof(NumericStringJsonConverter))]
    public decimal? Total { get; set; }
    public string PosData { get; set; }
}

