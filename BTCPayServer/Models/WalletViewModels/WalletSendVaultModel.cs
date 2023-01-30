namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSendVaultModel
    {
        public string WalletId { get; set; }
        public string WebsocketPath { get; set; }
        public string BackUrl { get; set; }
        public string ReturnUrl { get; set; }
        public SigningContextModel SigningContext { get; set; } = new();
    }
}
