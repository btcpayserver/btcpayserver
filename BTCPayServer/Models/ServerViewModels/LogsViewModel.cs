using System.Collections.Generic;
using System.IO;
using BTCPayServer.Services;
using BTCPayServer.Services.Mails;

namespace BTCPayServer.Models.ServerViewModels
{
    public class LogsViewModel
    {
        public List<FileInfo> LogFiles { get; set; } = new List<FileInfo>();
        public string Log { get; set; }
        public int LogFileCount { get; set; }
        public int LogFileOffset { get; set; }

        public LogSettings Settings { get; set; }

        public LogsViewModel()
        {

        }

        public LogsViewModel(LogSettings settings)
        {
            Settings = settings;
        }
    }
}
