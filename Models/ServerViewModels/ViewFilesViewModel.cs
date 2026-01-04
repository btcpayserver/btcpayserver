using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Models.ServerViewModels
{
    public class ViewFilesViewModel
    {
        public List<StoredFile> Files { get; set; }
        public Dictionary<string, string> DirectUrlByFiles { get; set; }
        public bool StorageConfigured { get; set; }
    }
}
