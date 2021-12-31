namespace BTCPayServer.Client.Models
{
    public class GenericPaymentMethodData
    {
        public bool Enabled { get; set; }
        public object Data { get; set; }
        public string CryptoCode { get; set; }
    }
}
