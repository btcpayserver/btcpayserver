using System;

namespace BTCPayServer.Storage
{
    public class File
    {
        public string Id { get; set; }

        public string FileName { get; set; }
        public string StorageFileName{ get; set; }
        public DateTime Timestamp { get; set; }
    }
}
