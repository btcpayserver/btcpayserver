namespace BTCPayServer.Client.Models
{
    public class PayoutProcessorData
    {
        public string Name { get; set; }
        public string FriendlyName { get; set; }
        public string[] PaymentMethods { get; set; }
    }
}
