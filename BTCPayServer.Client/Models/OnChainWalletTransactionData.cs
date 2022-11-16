using System;
using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    
    public class OnChainWalletObjectQuery
    {
        public string[]? Types { get; set; }
        public OnChainWalletObjectId[]? Parents { get; set; }
        public OnChainWalletObjectId[]? Children { get; set; }
            
        public bool IncludeLinks { get; set; }
    }


    
    public class OnChainWalletObjectId
    {
        public string Type { get; set; }
        public string Id { get; set; }
    }

    public class RemoveOnChainWalletObjectLinkRequest
    {
        public OnChainWalletObjectId Parent { get; set; }
        public OnChainWalletObjectId Child { get; set; }
    }
    public class AddOnChainWalletObjectLinkRequest
    {
        public OnChainWalletObjectId Parent { get; set; }
        public OnChainWalletObjectId Child { get; set; }
        public JObject? Data { get; set; }
    }
    

    public class OnChainWalletObjectData:OnChainWalletObjectId
    {
        public class OnChainWalletObjectLink:OnChainWalletObjectId
        {
            public JObject? LinkData { get; set; }
        }
        public JObject? Data { get; set; }
        public OnChainWalletObjectLink[]? Parents { get; set; }
        public OnChainWalletObjectLink[]? Children { get; set; }
    }
    
    public class OnChainWalletTransactionData
    {
        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 TransactionHash { get; set; }

        public string Comment { get; set; }
#pragma warning disable CS0612 // Type or member is obsolete
        public Dictionary<string, LabelData> Labels { get; set; } = new Dictionary<string, LabelData>();
#pragma warning restore CS0612 // Type or member is obsolete

        [JsonConverter(typeof(NumericStringJsonConverter))]
        public decimal Amount { get; set; }

        [JsonConverter(typeof(UInt256JsonConverter))]
        public uint256 BlockHash { get; set; }

        public long? BlockHeight { get; set; }

        public long Confirmations { get; set; }

        [JsonConverter(typeof(DateTimeToUnixTimeConverter))]
        public DateTimeOffset Timestamp { get; set; }

        [JsonConverter(typeof(Newtonsoft.Json.Converters.StringEnumConverter))]
        public TransactionStatus Status { get; set; }
    }
}
