using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Lightning.CLightning
{
    public class CLightningInvoice
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty("payment_hash")]
        public uint256 PaymentHash { get; set; }

        [JsonProperty("msatoshi")]
        [JsonConverter(typeof(JsonConverters.LightMoneyJsonConverter))]
        public LightMoney MilliSatoshi { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        [JsonProperty("expiry_time")]
        public DateTimeOffset ExpiryTime { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        [JsonProperty("expires_at")]
        public DateTimeOffset ExpiryAt { get; set; }
        [JsonProperty("bolt11")]
        public string BOLT11 { get; set; }
        [JsonProperty("pay_index")]
        public int? PayIndex { get; set; }
        public string Label { get; set; }
        public string Status { get; set; }
        [JsonProperty("paid_at")]
        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset? PaidAt { get; set; }
    }
}
