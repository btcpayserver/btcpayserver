using System;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Lightning;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models
{
    public class LightningPaymentData
    {
        public string Id { get; set; }

        public string PaymentHash { get; set; }

        [JsonConverter(typeof(StringEnumConverter))]
        public LightningPaymentStatus Status { get; set; }

        [JsonProperty("BOLT11")]
        public string BOLT11 { get; set; }

        public string Preimage { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? CreatedAt { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney TotalAmount { get; set; }

        [JsonConverter(typeof(LightMoneyJsonConverter))]
        public LightMoney FeeAmount { get; set; }
    }
}
