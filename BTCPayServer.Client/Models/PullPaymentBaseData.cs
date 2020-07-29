using System;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class PullPaymentData
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset StartsAt { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? ExpiresAt { get; set; }
        public string Id { get; set; }
        public string Name { get; set; }
        public string Currency { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }
        [JsonConverter(typeof(TimeSpanJsonConverter))]
        public TimeSpan? Period { get; set; }
        public bool Archived { get; set; }
        public string ViewLink { get; set; }
    }
}
