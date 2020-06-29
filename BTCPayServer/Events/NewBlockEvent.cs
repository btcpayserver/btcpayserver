namespace BTCPayServer.Events
{
    public class NewBlockEvent
    {
        public string CryptoCode { get; set; }
        public override string ToString()
        {
            return $"{CryptoCode}: New block";
        }
    }
}
