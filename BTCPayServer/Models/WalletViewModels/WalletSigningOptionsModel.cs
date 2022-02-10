using System.Collections.Generic;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSigningOptionsModel
    {
        public WalletSigningOptionsModel(
            SigningContextModel signingContext,
            string returnUrl)
        {
            SigningContext = signingContext;
            ReturnUrl = returnUrl;
        }

        public SigningContextModel SigningContext { get; }
        public string ReturnUrl { get; }
    }
}
