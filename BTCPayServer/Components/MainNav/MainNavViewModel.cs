using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;
using BTCPayServer.Services.Apps;

namespace BTCPayServer.Components.MainNav
{
    public class MainNavViewModel
    {
        public StoreData Store { get; set; }
        public List<StoreDerivationScheme> DerivationSchemes { get; set; }
        public List<StoreLightningNode> LightningNodes { get; set; }
        public List<StoreApp> Apps { get; set; }
        public CustodianAccountData[] CustodianAccounts { get; set; }
        public bool AltcoinsBuild { get; set; }
    }

    public class StoreApp
    {
        public string Id { get; set; }
        public string AppName { get; set; }
        public AppType AppType { get; set; }
        public bool IsOwner { get; set; }
    }
}
