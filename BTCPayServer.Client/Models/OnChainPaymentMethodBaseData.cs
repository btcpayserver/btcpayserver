using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class OnChainPaymentMethodBaseData
    {
        /// <summary>
        /// The derivation scheme
        /// </summary>
        public string DerivationScheme { get; set; }

        public string Label { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
        public RootedKeyPath AccountKeyPath { get; set; }

        public OnChainPaymentMethodBaseData()
        {
        }


    }
}
