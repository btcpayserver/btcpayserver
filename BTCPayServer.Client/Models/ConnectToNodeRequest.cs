namespace BTCPayServer.Client.Models
{
    public class ConnectToNodeRequest
    {
        public string NodeInfo { get; set; }
        public string NodeId { get; set; }
        public string NodeHost { get; set; }
        public int NodePort { get; set; }
    }
}
