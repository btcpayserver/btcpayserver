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
        public List<StoreApp> Apps { get; set; }
        public int ArchivedAppsCount { get; set; }
        public string ContactUrl { get; set; }
        public string UserName { get; set; }
        public string UserImageUrl { get; set; }
    }

    public class StoreApp
    {
        public string Id { get; set; }
        public string AppName { get; set; }
        public string AppType { get; set; }
        public AppData Data { get; set; }
    }
}
