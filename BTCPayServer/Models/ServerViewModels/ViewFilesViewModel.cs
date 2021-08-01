using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Models.ServerViewModels
{
    public class ViewFilesViewModel
    {
        public List<StoredFile> Files { get; set; }
        public List<string> DirectFileUrls { get; set; }
        public List<string> SelectedFileIds { get; set; }
        public bool StorageConfigured { get; set; }
    }
}
