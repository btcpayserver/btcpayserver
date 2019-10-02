namespace BTCPayServer.U2F.Models
{
    public class ServerRegisterResponse
    {
        public string AppId { get; set; }
        public string Challenge { get; set; }
        public string Version { get; set; }
    }
}
