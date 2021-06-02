using System.Collections.Generic;

namespace BTCPayServer.Models.WalletViewModels
{
    public class WalletSigningOptionsModel
    {
        public WalletSigningOptionsModel(
            SigningContextModel signingContext, 
            IDictionary<string, string> routeDataBack, 
            IDictionary<string, string> routeDataForm)
        {
            SigningContext = signingContext;
            RouteDataBack = routeDataBack;
            RouteDataForm = routeDataForm;
        }

        public SigningContextModel SigningContext { get; }
        public IDictionary<string, string> RouteDataBack { get; }
        public IDictionary<string, string> RouteDataForm { get; }
    }
}
