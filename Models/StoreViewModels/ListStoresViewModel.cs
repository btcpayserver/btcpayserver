using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Models.StoreViewModels;

public class ListStoresViewModel
{
    public class StoreViewModel
    {
        public string StoreName { get; set; }
        public string StoreId { get; set; }
        public bool Archived { get; set; }
        public List<UserStore> Users { get; set; }
    }

    public List<StoreViewModel> Stores { get; set; } = new ();
    public bool Archived { get; set; }
}
