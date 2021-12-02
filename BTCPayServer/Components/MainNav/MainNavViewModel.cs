using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Models.StoreViewModels;

namespace BTCPayServer.Components.MainNav
{
    public class MainNavViewModel
    {
        public StoreData Store { get; set; }
        public List<StoreDerivationScheme> DerivationSchemes { get; set; }
        public List<StoreLightningNode> LightningNodes { get; set; }
    }
}
