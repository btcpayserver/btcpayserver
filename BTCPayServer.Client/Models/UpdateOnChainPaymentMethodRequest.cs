using NBitcoin;

namespace BTCPayServer.Client.Models
{
    public class UpdateOnChainPaymentMethodRequest : OnChainPaymentMethodBaseData
    {
        /// <summary>
        /// Whether the payment method is enabled
        /// </summary>
        public bool Enabled { get; set; }

        public UpdateOnChainPaymentMethodRequest()
        {

        }

        public UpdateOnChainPaymentMethodRequest(bool enabled, string derivationScheme, string label, RootedKeyPath accountKeyPath)
        {
            Enabled = enabled;
            Label = label;
            AccountKeyPath = accountKeyPath;
            DerivationScheme = derivationScheme;
        }
    }
}
