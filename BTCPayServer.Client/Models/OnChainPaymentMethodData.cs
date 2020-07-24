namespace BTCPayServer.Client.Models
{
    public class OnChainPaymentMethodData
    {
        /// <summary>
        /// Whether the payment method is enabled
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Whether the payment method is the default
        /// </summary>
        public bool Default { get; set; }

        /// <summary>
        /// Crypto code of the payment method
        /// </summary>
        public string CryptoCode { get; set; }

        /// <summary>
        /// The derivation scheme
        /// </summary>
        public string DerivationScheme { get; set; }

        public OnChainPaymentMethodData()
        {
        }

        public OnChainPaymentMethodData(string cryptoCode, string derivationScheme, bool enabled,
            bool defaultMethod)
        {
            Enabled = enabled;
            Default = defaultMethod;
            CryptoCode = cryptoCode;
            DerivationScheme = derivationScheme;
        }
    }
}
