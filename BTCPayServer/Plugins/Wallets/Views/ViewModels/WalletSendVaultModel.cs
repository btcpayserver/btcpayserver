using BTCPayServer.Models;

namespace BTCPayServer.Plugins.Wallets.Views.ViewModels
{
    public class WalletSendVaultModel : IHasBackAndReturnUrl
    {
        public string WalletId { get; set; }
        public string BackUrl { get; set; }
        public string ReturnUrl { get; set; }
        public SigningContextModel SigningContext { get; set; } = new();
    }
}
