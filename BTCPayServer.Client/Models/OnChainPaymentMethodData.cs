using NBitcoin;

namespace BTCPayServer.Client.Models
{
    public class OnChainPaymentMethodData : OnChainPaymentMethodBaseData
    {
        /// <summary>
        /// Whether the payment method is enabled
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Crypto code of the payment method
        /// </summary>
        public string CryptoCode { get; set; }

        public OnChainPaymentMethodData()
        {
            
        }

        public OnChainPaymentMethodData(string cryptoCode, string derivationScheme, bool enabled, string label, RootedKeyPath accountKeyPath)
        {
            Enabled = enabled;
            Label = label;
            AccountKeyPath = accountKeyPath;
            CryptoCode = cryptoCode;
            DerivationScheme = derivationScheme;
        }
    }
}
