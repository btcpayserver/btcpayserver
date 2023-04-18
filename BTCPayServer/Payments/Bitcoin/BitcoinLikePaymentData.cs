using BTCPayServer.Client.Models;
using BTCPayServer.Services.Invoices;
using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Payments.Bitcoin
{

    public class BitcoinLikePaymentData : CryptoPaymentData
    {
        public PaymentType GetPaymentType()
        {
            return PaymentTypes.BTCLike;
        }
        public BitcoinLikePaymentData()
        {

        }

        public BitcoinLikePaymentData(BitcoinAddress address, IMoney value, OutPoint outpoint, bool rbf, KeyPath keyPath)
        {
            Address = address;
            Value = value;
            Outpoint = outpoint;
            ConfirmationCount = 0;
            RBF = rbf;
            KeyPath = keyPath;
        }
        [JsonIgnore]
        public BTCPayNetworkBase Network { get; set; }
        [JsonIgnore]
        public OutPoint Outpoint { get; set; }
        [JsonIgnore]
        public TxOut Output { get; set; }
        public long ConfirmationCount { get; set; }
        public bool RBF { get; set; }
        public BitcoinAddress Address { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
        public KeyPath KeyPath { get; set; }
        public IMoney Value { get; set; }

        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public PayjoinInformation PayjoinInformation { get; set; }

        [JsonIgnore]
        public Script ScriptPubKey
        {
            get
            {
                return Address?.ScriptPubKey ?? Output.ScriptPubKey;
            }
        }

        /// <summary>
        /// This is set to true if the payment was created before CryptoPaymentData existed in BTCPayServer
        /// </summary>
        public bool Legacy { get; set; }

        public string GetPaymentId()
        {
            return Outpoint.ToString();
        }

        public string[] GetSearchTerms()
        {
            return new[] { Outpoint.Hash.ToString() };
        }

        public decimal GetValue()
        {
            return Value?.GetValue(Network as BTCPayNetwork) ?? Output.Value.ToDecimal(MoneyUnit.BTC);
        }

        public bool PaymentCompleted(PaymentEntity entity)
        {
            return ConfirmationCount >= ((BTCPayNetwork)Network).MaxTrackedConfirmation;
        }

        public bool PaymentConfirmed(PaymentEntity entity, SpeedPolicy speedPolicy)
        {
            if (speedPolicy == SpeedPolicy.HighSpeed)
            {
                return ConfirmationCount >= 1 || !RBF;
            }
            else if (speedPolicy == SpeedPolicy.MediumSpeed)
            {
                return ConfirmationCount >= 1;
            }
            else if (speedPolicy == SpeedPolicy.LowMediumSpeed)
            {
                return ConfirmationCount >= 2;
            }
            else if (speedPolicy == SpeedPolicy.LowSpeed)
            {
                return ConfirmationCount >= 6;
            }
            return false;
        }

        public BitcoinAddress GetDestination()
        {
            return Address ?? Output.ScriptPubKey.GetDestinationAddress(((BTCPayNetwork)Network).NBitcoinNetwork);
        }

        string CryptoPaymentData.GetDestination()
        {
            return GetDestination().ToString();
        }

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
