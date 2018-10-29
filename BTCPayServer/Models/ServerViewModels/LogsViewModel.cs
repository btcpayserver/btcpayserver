using System.Collections.Generic;
using System.IO;

namespace BTCPayServer.Models.ServerViewModels
{
    public class LogsViewModel
    {
        
        public string StatusMessage
        {
            get; set;
        }

        public List<FileInfo> LogFiles { get; set; }
        public string Log { get; set; }
    }
}
