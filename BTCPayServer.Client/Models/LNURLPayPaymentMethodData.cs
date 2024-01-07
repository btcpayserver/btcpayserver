namespace BTCPayServer.Client.Models
{
    public class LNURLPayPaymentMethodData : LNURLPayPaymentMethodBaseData
    {
        /// <summary>
        /// Whether the payment method is enabled
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Crypto code of the payment method
        /// </summary>
        public string CryptoCode { get; set; }

        public LNURLPayPaymentMethodData()
        {
        }

        public LNURLPayPaymentMethodData(string cryptoCode, bool enabled, bool useBech32Scheme, bool lud12Enabled)
        {
            Enabled = enabled;
            CryptoCode = cryptoCode;
            UseBech32Scheme = useBech32Scheme;
            LUD12Enabled = lud12Enabled;
        }
    }
}
