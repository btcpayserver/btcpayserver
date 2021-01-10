using NBitcoin;
using Newtonsoft.Json;

namespace BTCPayServer.Client.Models
{
    public class OnChainPaymentMethodData
    {
        /// <summary>
        /// Whether the payment method is enabled
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Crypto code of the payment method
        /// </summary>
        public string CryptoCode { get; set; }

        /// <summary>
        /// The derivation scheme
        /// </summary>
        public string DerivationScheme { get; set; }

        public string Label { get; set; }

        [JsonConverter(typeof(NBitcoin.JsonConverters.KeyPathJsonConverter))]
        public RootedKeyPath AccountKeyPath { get; set; }

        public OnChainPaymentMethodData()
        {
        }

        public OnChainPaymentMethodData(string cryptoCode, string derivationScheme, bool enabled)
        {
            Enabled = enabled;
            CryptoCode = cryptoCode;
            DerivationScheme = derivationScheme;
        }
    }
}
