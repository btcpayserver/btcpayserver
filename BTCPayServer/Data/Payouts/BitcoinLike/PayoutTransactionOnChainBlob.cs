using System.Collections.Generic;
using System.Globalization;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Data
{
    public class PayoutTransactionOnChainBlob : IPayoutProof
    {
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        public uint256 TransactionId { get; set; }
        [JsonProperty(ItemConverterType = typeof(NBitcoin.JsonConverters.UInt256JsonConverter), NullValueHandling = NullValueHandling.Ignore)]
        public HashSet<uint256> Candidates { get; set; } = new HashSet<uint256>();

        [JsonIgnore] public string LinkTemplate { get; set; }
        public string ProofType { get; } = Type;
        public const string Type = "PayoutTransactionOnChainBlob";

        [JsonIgnore]
        public string Link
        {
            get { return Id != null ? string.Format(CultureInfo.InvariantCulture, LinkTemplate, Id) : null; }
        }
        public bool? Accounted { get; set; }//nullable to be backwards compatible. if null, accounted is true
        [JsonIgnore]
        public string Id { get { return TransactionId?.ToString(); } }
    }
}
