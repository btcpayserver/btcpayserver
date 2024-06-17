namespace BTCPayServer.Events
{
    public class NewBlockEvent
    {
        public string CryptoCode { get; set; }
        public int Height { get; set; }
        public override string ToString()
        {
            return $"{CryptoCode}: New block";
        }
    }
}
