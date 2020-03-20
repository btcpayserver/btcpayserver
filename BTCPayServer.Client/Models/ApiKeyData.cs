namespace BTCPayServer.Client.Models
{
    public class ApiKeyData
    {
        public string ApiKey { get; set; }
        public string Label { get; set; }
        public string UserId { get; set; }
        public Permission[] Permissions { get; set; }
    }
}
