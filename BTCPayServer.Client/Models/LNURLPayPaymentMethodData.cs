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

        public LNURLPayPaymentMethodData(string cryptoCode, bool enabled, bool useBech32Scheme, bool enableForStandardInvoices)
        {
            Enabled = enabled;
            CryptoCode = cryptoCode;
            UseBech32Scheme = useBech32Scheme;
            EnableForStandardInvoices = enableForStandardInvoices;
        }
    }
}
