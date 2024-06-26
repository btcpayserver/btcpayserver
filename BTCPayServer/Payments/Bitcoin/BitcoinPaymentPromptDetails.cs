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

        [JsonConverter(typeof(NBitcoin.JsonConverters.FeeRateJsonConverter))]
        public FeeRate PaymentMethodFeeRate
        {
            get;
            set;
        }
        public bool PayjoinEnabled { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.FeeRateJsonConverter))]
        public FeeRate RecommendedFeeRate { get; set; }
        [JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
        public KeyPath KeyPath { get; set; }
        public DerivationStrategyBase AccountDerivation { get; set; }
    }
}
