using System;

namespace BTCPayServer.Storage.Services.Providers.FileSystemStorage
{
    public class TemporaryLocalFileDescriptor
    {
        public string FileId { get; set; }
        public bool IsDownload { get; set; }
        public DateTimeOffset Expiry { get; set; }
    }
}
