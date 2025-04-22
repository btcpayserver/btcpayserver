namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSendVaultModel : IHasBackAndReturnUrl
    {
        public string WalletId { get; set; }
        public string BackUrl { get; set; }
        public string ReturnUrl { get; set; }
        public SigningContextModel SigningContext { get; set; } = new();
    }
}
