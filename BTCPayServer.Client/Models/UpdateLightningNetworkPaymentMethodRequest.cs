namespace BTCPayServer.Client.Models
{
    public class UpdateLightningNetworkPaymentMethodRequest : LightningNetworkPaymentMethodBaseData
    {
        /// <summary>
        /// Whether the payment method is enabled
        /// </summary>
        public bool Enabled { get; set; }

        public UpdateLightningNetworkPaymentMethodRequest()
        {
        }

        public UpdateLightningNetworkPaymentMethodRequest(string connectionString, bool enabled)
        {
            Enabled = enabled;
            ConnectionString = connectionString;
        }
    }
}
