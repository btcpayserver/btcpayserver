using System;
using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class OnChainWalletUTXOData
    {
        public string Comment { get; set; }
        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }
        [JsonConverter(typeof(OutpointJsonConverter))]
        public OutPoint Outpoint { get; set; }
        public string Link { get; set; }
        public Dictionary<string, LabelData> Labels { get; set; }
        [JsonConverter(typeof(DateTimeToUnixTimeConverter))]
        public DateTimeOffset Timestamp { get; set; }
        [JsonConverter(typeof(KeyPathJsonConverter))]
        public KeyPath KeyPath { get; set; }
        public string Address { get; set; }
    }
}
