using System;
using System.Collections.Generic;
using BTCPayServer.JsonConverters;
using NBitcoin;
using NBitcoin.JsonConverters;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Client.Models
{
    public class OnChainWalletObjectId
    {
        public OnChainWalletObjectId()
        {

        }
        public OnChainWalletObjectId(string type, string id)
        {
            Type = type;
            Id = id;
        }
        public string Type { get; set; }
        public string Id { get; set; }
    }
    public class AddOnChainWalletObjectLinkRequest : OnChainWalletObjectId
    {
        public AddOnChainWalletObjectLinkRequest()
        {

        }
        public AddOnChainWalletObjectLinkRequest(string objectType, string objectId) : base(objectType, objectId)
        {

        }
        public JObject Data { get; set; }
    }

    public class GetWalletObjectsRequest
    {
        public string Type { get; set; }
        public string[] Ids { get; set; }
        public bool? IncludeNeighbourData { get; set; }
    }

    public class AddOnChainWalletObjectRequest : OnChainWalletObjectId
    {
        public AddOnChainWalletObjectRequest()
        {

        }
        public AddOnChainWalletObjectRequest(string objectType, string objectId) : base(objectType, objectId)
        {

        }
        public JObject Data { get; set; }
    }

    public class OnChainWalletObjectData : OnChainWalletObjectId
    {
        public OnChainWalletObjectData()
        {

        }
        public OnChainWalletObjectData(string type, string id) : base(type, id)
        {

        }

        public class OnChainWalletObjectLink : OnChainWalletObjectId
        {
            public OnChainWalletObjectLink()
            {

            }
            public OnChainWalletObjectLink(string type, string id) : base(type, id)
            {

            }
            public JObject LinkData { get; set; }
            public JObject ObjectData { get; set; }
        }
        public JObject Data { get; set; }
        public OnChainWalletObjectLink[] Links { get; set; }
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
