using System.Collections.Generic;
using BTCPayServer.Configuration;
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
        public List<AdditionalService> Services { get; set; }
        public bool AltcoinsBuild { get; set; }
    }
    
    public class AdditionalService
    {
        public string Type { get; set; }
        public string DisplayName { get; set; }
        public string ServiceName { get; set; }
        public string CryptoCode { get; set; }
        public string Link { get; set; }
    }

    public class StoreApp
    {
        public string Id { get; set; }
        public string AppName { get; set; }
        public string AppType { get; set; }
        public string Action { get => $"Update{AppType}"; }
        public bool IsOwner { get; set; }
    }
}
