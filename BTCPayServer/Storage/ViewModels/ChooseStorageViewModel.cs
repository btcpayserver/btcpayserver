using BTCPayServer.Storage.Models;

namespace BTCPayServer.Storage.ViewModels
{
    public class ChooseStorageViewModel
    {
        public StorageProvider Provider { get; set; }
        public bool ShowChangeWarning { get; set; }
    }
}
