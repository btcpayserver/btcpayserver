namespace BTCPayServer.Models.ServerViewModels
{
    public class SSHServiceViewModel
    {
        public string CommandLine { get; set; }
        public string Password { get; set; }
        public string KeyFilePassword { get; set; }
        public bool HasKeyFile { get; set; }
        public string SSHKeyFileContent { get; set; }
    }
}
