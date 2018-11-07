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

        public List<FileInfo> LogFiles { get; set; } = new List<FileInfo>();
        public string Log { get; set; }
        public int LogFileCount { get; set; }
        public int LogFileOffset{ get; set; }
    }
}
