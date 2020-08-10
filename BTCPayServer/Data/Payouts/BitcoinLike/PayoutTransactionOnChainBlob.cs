using System.Collections.Generic;
using System.Globalization;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class PayoutTransactionOnChainBlob: IPayoutProof
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 TransactionId { get; set; }
        [JsonProperty(ItemConverterType = typeof(NBitcoin.JsonConverters.UInt256JsonConverter), NullValueHandling = NullValueHandling.Ignore)]
        public HashSet<uint256> Candidates { get; set; } = new HashSet<uint256>();

        [JsonIgnore] public string LinkTemplate { get; set; }
        [JsonIgnore]
        public string Link
        {
            get { return Id != null ? string.Format(CultureInfo.InvariantCulture, LinkTemplate, Id) : null; }
        }
        [JsonIgnore]
        public string Id { get { return TransactionId?.ToString(); } }
    }
}
