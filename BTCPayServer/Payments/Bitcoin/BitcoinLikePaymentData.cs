using BTCPayServer.Client.JsonConverters;
using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Bitcoin
{

    public class BitcoinLikePaymentData : CryptoPaymentData
    {
        public BitcoinLikePaymentData()
        {

        }

        public BitcoinLikePaymentData(OutPoint outpoint, bool rbf, KeyPath keyPath)
        {
            Outpoint = outpoint;
            ConfirmationCount = 0;
            RBF = rbf;
            KeyPath = keyPath;
        }
        [JsonConverter(typeof(SaneOutpointJsonConverter))]
        public OutPoint Outpoint { get; set; }
        public long ConfirmationCount { get; set; }
        [JsonProperty("RBF")]
        public bool RBF { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
        public KeyPath KeyPath { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public PayjoinInformation PayjoinInformation { get; set; }

        string CryptoPaymentData.GetPaymentProof()
        {
            return Outpoint?.ToString();
        }
    }


    public class PayjoinInformation
    {
        public uint256 CoinjoinTransactionHash { get; set; }
        public Money CoinjoinValue { get; set; }
        public OutPoint[] ContributedOutPoints { get; set; }
    }
}
