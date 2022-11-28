#nullable enable
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public enum RefundVariant
    {
        RateThen,
        CurrentRate,
        Fiat,
        Custom,
        NotSet
    }

    public class RefundInvoiceRequest
    {
        public string? Name { get; set; } = null;
        public string? Description { get; set; } = null;
        [JsonConverter(typeof(StringEnumConverter))]
        public RefundVariant RefundVariant { get; set; } = RefundVariant.NotSet;
        public decimal CustomAmount { get; set; } = 0;
        public string? CustomCurrency { get; set; } = null;
    }
}
