using System;
using System.Collections.Generic;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class OnChainWalletOverviewData
    {
        public decimal Balance { get; set; }
    }

    public class OnChainWalletTransactionData
    {
        public string Comment { get; set; }
        public Dictionary<string, LabelData> Labels { get; set; }
        public decimal Amount { get; set; }

        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 BlockHash { get; set; }

        public int? BlockHeight { get; set; }
        public int Confirmations { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.DateTimeToUnixTimeConverter))]
        public DateTimeOffset Timestamp { get; set; }

        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public TransactionStatus Status { get; set; }
    }

    public class LabelData
    {
        public string Type { get; set; }
        public string Text { get; set; }

        [JsonExtensionData] public Dictionary<string, JToken> AdditionalData { get; set; }
    }
    
    

    public enum TransactionStatus
    {
        Unconfirmed,
        Confirmed,
        Replaced
    }
}
