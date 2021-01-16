namespace BTCPayServer.Client.Models
{
    public class LightningNetworkPaymentMethodData
    {
        /// <summary>
        /// Whether the payment method is enabled
        /// </summary>
        public bool Enabled { get; set; }

        /// <summary>
        /// Crypto code of the payment method
        /// </summary>
        public string CryptoCode { get; set; }

        public string ConnectionString { get; set; }

        public LightningNetworkPaymentMethodData()
        {
        }

        public LightningNetworkPaymentMethodData(string cryptoCode, string connectionString, bool enabled)
        {
            Enabled = enabled;
            CryptoCode = cryptoCode;
            ConnectionString = connectionString;
        }
    }
}
