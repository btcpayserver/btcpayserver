using System.Collections.Generic;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSigningOptionsModel
    {
        public WalletSigningOptionsModel(
            SigningContextModel signingContext, 
            IDictionary<string, string> routeDataBack)
        {
            SigningContext = signingContext;
            RouteDataBack = routeDataBack;
        }

        public SigningContextModel SigningContext { get; }
        public IDictionary<string, string> RouteDataBack { get; }
    }
}
