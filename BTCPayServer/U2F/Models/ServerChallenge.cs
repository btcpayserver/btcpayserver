namespace BTCPayServer.U2F.Models
{
    public class ServerChallenge
    {
        public string challenge { get; set; }
        public string version { get; set; }
        public string appId { get; set; }
        public string keyHandle { get; set; }
    }
}
