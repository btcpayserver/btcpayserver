namespace BTCPayServer.Client.Models
{
    public class ServerEmailSettingsData
    {
        public bool EnableStoresToUseServerEmailSettings { get; set; }
        
        public EmailSettingsData Settings { get; set; }
    }
}
