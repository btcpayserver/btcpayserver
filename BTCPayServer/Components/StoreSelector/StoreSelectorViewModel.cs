using System.Collections.Generic;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Components.StoreSelector
{
    public class StoreSelectorViewModel
    {
        public List<SelectListItem> Options { get; set; }
        public string CurrentStoreId { get; set; }
        public string CurrentDisplayName { get; set; }
    }
}
