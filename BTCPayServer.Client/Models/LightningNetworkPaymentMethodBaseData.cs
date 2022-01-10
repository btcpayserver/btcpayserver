namespace BTCPayServer.Client.Models
{
    public class LightningNetworkPaymentMethodBaseData
    {

        public string ConnectionString { get; set; }
        public bool DisableBOLT11PaymentOption { get; set; }
        public LightningNetworkPaymentMethodBaseData()
        {

        }
    }
}
