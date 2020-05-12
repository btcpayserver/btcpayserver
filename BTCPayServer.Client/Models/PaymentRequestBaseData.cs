using System;
using BTCPayServer.Client.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class PaymentRequestBaseData
    {
        [JsonProperty(ItemConverterType = typeof(PermissionJsonConverter))]
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        public DateTime? ExpiryDate { get; set; }
        public string Title { get; set; }
        public string Description { get; set; }
        public string Email { get; set; }

        public string EmbeddedCSS { get; set; }
        public string CustomCSSLink { get; set; }
        public bool AllowCustomPaymentAmounts { get; set; }
    }
}
