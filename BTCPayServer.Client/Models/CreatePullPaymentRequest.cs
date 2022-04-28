using System;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class CreatePullPaymentRequest
    {
        public string Name { get; set; }
        public string Description { get; set; }
        [JsonProperty(ItemConverterType = typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }
        public string Currency { get; set; }
        [JsonConverter(typeof(TimeSpanJsonConverter.Seconds))]
        public TimeSpan? Period { get; set; }
        [JsonConverter(typeof(TimeSpanJsonConverter.Days))]
        [JsonProperty("BOLT11Expiration")]
        public TimeSpan? BOLT11Expiration { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? ExpiresAt { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? StartsAt { get; set; }
        public string[] PaymentMethods { get; set; }
        public bool AutoApproveClaims { get; set; }
    }
}
