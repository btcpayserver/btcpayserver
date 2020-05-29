using System;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public class LightningInvoiceData
    {
        public string Id { get; set; }

        [JsonProperty(ItemConverterType = typeof(StringEnumConverter))]
        public LightningInvoiceStatus Status { get; set; }

        public string BOLT11 { get; set; }

        public DateTimeOffset? PaidAt { get; set; }

        public DateTimeOffset ExpiresAt { get; set; }

        [JsonProperty(ItemConverterType = typeof(LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        [JsonProperty(ItemConverterType = typeof(LightMoneyJsonConverter))]
        public LightMoney AmountReceived { get; set; }
    }
}
