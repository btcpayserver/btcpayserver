#nullable enable
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public enum RefundVariant
    {
        RateThen,
        CurrentRate,
        OverpaidAmount,
        Fiat,
        Custom
    }

    public class RefundInvoiceRequest
    {
        public string? Name { get; set; } = null;
        public string? PaymentMethod { get; set; }
        public string? Description { get; set; } = null;
        
        [JsonConverter(typeof(StringEnumConverter))]
        public RefundVariant? RefundVariant { get; set; }
        
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal SubtractPercentage { get; set; }
        
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal? CustomAmount { get; set; }
        public string? CustomCurrency { get; set; }
    }
}
