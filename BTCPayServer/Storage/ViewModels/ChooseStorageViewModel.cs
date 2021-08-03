using System.Collections.Generic;
using BTCPayServer.Storage.Models;
using Microsoft.AspNetCore.Mvc.Rendering;

namespace BTCPayServer.Storage.ViewModels
{
    public class ChooseStorageViewModel
    {
        public IEnumerable<SelectListItem> ProvidersList { get; set; }
        public StorageProvider Provider { get; set; }
        public bool ShowChangeWarning { get; set; }
    }
}
