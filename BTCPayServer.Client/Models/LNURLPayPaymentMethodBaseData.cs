namespace BTCPayServer.Client.Models
{
    public class LNURLPayPaymentMethodBaseData
    {
        public bool UseBech32Scheme { get; set; }
        public bool EnableForStandardInvoices { get; set; }
        public LNURLPayPaymentMethodBaseData()
        {
            
        }
    }
}
