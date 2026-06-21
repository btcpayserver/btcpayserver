#nullable enable
using System;
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

        [Obsolete("Use PayoutMethods instead")]
        public string? PayoutMethodId { get; set; }

        /// <summary>
        /// The payout methods the refund can be claimed in. If null, falls back to <see cref="PayoutMethodId"/>,
        /// and if that is also null, to the default payout method of the original invoice.
        /// </summary>
        public string[]? PayoutMethods { get; set; }
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
