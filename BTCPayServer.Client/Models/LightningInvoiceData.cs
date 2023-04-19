using System;
using System.Collections.Generic;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public class LightningInvoiceData
    {
        public string Id { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public LightningInvoiceStatus Status { get; set; }

        [JsonProperty("BOLT11")]
        public string BOLT11 { get; set; }

        public string PaymentHash { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public string Preimage { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? PaidAt { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset ExpiresAt { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney Amount { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney AmountReceived { get; set; }

        [JsonProperty(NullValueHandling = NullValueHandling.Ignore)]
        public Dictionary<ulong, string> CustomRecords { get; set; }
    }
}
