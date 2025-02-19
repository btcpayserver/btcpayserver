namespace BTCPayServer.Client.Models
{
    public class ServerEmailSettingsData
    {
        // we don't want to expose the password over API get requests so we provide mask
        public const string PasswordMask = "******";
        
        public bool EnableStoresToUseServerEmailSettings { get; set; }
        
        public EmailSettingsData Settings { get; set; }
    }
}
