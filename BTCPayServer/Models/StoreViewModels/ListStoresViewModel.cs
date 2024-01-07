using System.Collections.Generic;

namespace BTCPayServer.Models.StoreViewModels;

public class ListStoresViewModel
{
    public class StoreViewModel
    {
        public string StoreName { get; set; }
        public string StoreId { get; set; }
        public bool Archived { get; set; }
    }

    public List<StoreViewModel> Stores { get; set; } = new ();
    public bool Archived { get; set; }
}
