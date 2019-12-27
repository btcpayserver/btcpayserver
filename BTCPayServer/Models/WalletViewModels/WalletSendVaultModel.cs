namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSendVaultModel
    {
        public string WalletId { get; set; }
        public string PSBT { get; set; }
        public string WebsocketPath { get; set; }
    }
}
