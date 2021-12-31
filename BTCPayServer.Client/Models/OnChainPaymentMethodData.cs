using NBitcoin;

namespace BTCPayServer.Client.Models
{
    public class OnChainPaymentMethodDataPreview : OnChainPaymentMethodBaseData
    {
        /// <summary>
        /// Crypto code of the payment method
        /// </summary>
        public string CryptoCode { get; set; }

        public OnChainPaymentMethodDataPreview()
        {

        }

        public OnChainPaymentMethodDataPreview(string cryptoCode, string derivationScheme, string label, RootedKeyPath accountKeyPath)
        {
            Label = label;
            AccountKeyPath = accountKeyPath;
            CryptoCode = cryptoCode;
            DerivationScheme = derivationScheme;
        }
    }

    public class OnChainPaymentMethodData : OnChainPaymentMethodDataPreview
    {
        /// <summary>
        /// Whether the payment method is enabled
        /// </summary>
        public bool Enabled { get; set; }

        public string PaymentMethod { get; set; }

        public OnChainPaymentMethodData()
        {

        }

        public OnChainPaymentMethodData(string cryptoCode, string derivationScheme, bool enabled, string label, RootedKeyPath accountKeyPath, string paymentMethod) :
            base(cryptoCode, derivationScheme, label, accountKeyPath)
        {
            Enabled = enabled;
            PaymentMethod = paymentMethod;
        }
    }
}
