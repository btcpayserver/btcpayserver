using System.Linq;
using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Bitcoin
{

    public class BitcoinLikePaymentData
    {
        public BitcoinLikePaymentData()
        {

        }

        public BitcoinLikePaymentData(OutPoint outpoint, bool rbf, KeyPath keyPath, int keyIndex)
        {
            if (keyPath != null)
                // This shouldn't be needed on new version of NBXplorer, but old version of NBXplorer
                // are not returning KeyIndex, and it is thus set to '0'.
                keyIndex = (int)keyPath.Indexes.Last();

            Outpoint = outpoint;
            ConfirmationCount = 0;
            RBF = rbf;
            KeyPath = keyPath;
            KeyIndex = keyIndex;
        }
        [JsonConverter(typeof(SaneOutpointJsonConverter))]
        public OutPoint Outpoint { get; set; }
        public long ConfirmationCount { get; set; }
        [JsonProperty("RBF")]
        public bool RBF { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
        public KeyPath KeyPath { get; set; }

        public int? KeyIndex { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public uint256 AssetId { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public PayjoinInformation PayjoinInformation { get; set; }
    }


    public class PayjoinInformation
    {
        public uint256 CoinjoinTransactionHash { get; set; }
        public Money CoinjoinValue { get; set; }
        public OutPoint[] ContributedOutPoints { get; set; }
    }
}
