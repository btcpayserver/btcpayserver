using System.Collections.Generic;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class PayoutTransactionOnChainBlob
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 TransactionId { get; set; }
        [JsonProperty(ItemConverterType = typeof(NBitcoin.JsonConverters.UInt256JsonConverter), NullValueHandling = NullValueHandling.Ignore)]
        public HashSet<uint256> Candidates { get; set; } = new HashSet<uint256>();
    }
}
