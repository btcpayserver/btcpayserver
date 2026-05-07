using BTCPayServer.Models;

namespace BTCPayServer.Plugins.Wallets.Views.ViewModels
{
    public class WalletSigningOptionsModel : IHasBackAndReturnUrl
    {
        public SigningContextModel SigningContext { get; set; }
        public string BackUrl { get; set; }
        public string ReturnUrl { get; set; }
    }
}
