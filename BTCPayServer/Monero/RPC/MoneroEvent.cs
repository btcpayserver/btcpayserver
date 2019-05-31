namespace BTCPayServer.Payments.Monero
{
    public class MoneroEvent
    {
        public string BlockHash { get; set; }
        public string TransactionHash { get; set; }
        public string CryptoCode { get; set; }
    }
}