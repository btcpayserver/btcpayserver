using System.Collections.Generic;

namespace BTCPayServer.Models.StoreViewModels
{
    public class StoresViewModel
    {
        public List<StoreViewModel> Stores { get; set; } = new List<StoreViewModel>();

        public class StoreViewModel
        {
            public string Id { get; set; }
            public string Name { get; set; }
            public string WebSite { get; set; }
            public bool IsOwner { get; set; }
            public bool HintWalletWarning { get; set; }
        }
    }
}
