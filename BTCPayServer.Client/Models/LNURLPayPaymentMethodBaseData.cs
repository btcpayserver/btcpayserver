namespace BTCPayServer.Client.Models
{
    public class LNURLPayPaymentMethodBaseData
    {
        public bool UseBech32Scheme { get; set; }
        public bool LUD12Enabled { get; set; }

        public LNURLPayPaymentMethodBaseData()
        {

        }
    }
}
