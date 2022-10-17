using System.Collections.Generic;

namespace BTCPayServer.Components.StoreSelector
{
    public class StoreSelectorViewModel
    {
        public List<StoreSelectorOption> Options { get; set; }
        public string CurrentStoreId { get; set; }
        public string CurrentStoreLogoFileId { get; set; }
        public string CurrentDisplayName { get; set; }
        public bool CurrentStoreIsOwner { get; set; }
    }

    public class StoreSelectorOption
    {
        public bool Selected { get; set; }
        public bool IsOwner { get; set; }
        public string Text { get; set; }
        public string Value { get; set; }
        public WalletId WalletId { get; set; }
    }
}
