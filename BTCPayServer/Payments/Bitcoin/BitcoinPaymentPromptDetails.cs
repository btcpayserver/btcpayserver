using System;
using System.ComponentModel;
using System.Linq;
using BTCPayServer.Client.Models;
using NBitcoin;
using NBXplorer.DerivationStrategy;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Payments.Bitcoin
{
    public class BitcoinPaymentPromptDetails
    {
        [JsonConverter(typeof(StringEnumConverter))]
        public NetworkFeeMode FeeMode { get; set; }

        /// <summary>
        /// The fee rate charged to the user as `PaymentMethodFee`.
        /// </summary>
        [JsonConverter(typeof(NBitcoin.JsonConverters.FeeRateJsonConverter))]
        public FeeRate PaymentMethodFeeRate
        {
            get;
            set;
        }
        [JsonConverter(typeof(NBitcoin.JsonConverters.UInt256JsonConverter))]
        [JsonProperty(DefaultValueHandling = DefaultValueHandling.Ignore)]
        public uint256 AssetId { get; set; }
        public bool PayjoinEnabled { get; set; }

        /// <summary>
        /// The recommended fee rate for this payment method.
        /// </summary>
        [JsonConverter(typeof(NBitcoin.JsonConverters.FeeRateJsonConverter))]
        public FeeRate RecommendedFeeRate { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
        public KeyPath KeyPath { get; set; }
        public DerivationStrategyBase AccountDerivation { get; set; }
    }
}
