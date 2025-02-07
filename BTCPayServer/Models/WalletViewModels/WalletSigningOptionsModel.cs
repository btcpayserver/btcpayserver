using System;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSigningOptionsModel : IHasBackAndReturnUrl
    {
        public SigningContextModel SigningContext { get; set; }
        public string BackUrl { get; set; }
        public string ReturnUrl { get; set; }
    }
}
