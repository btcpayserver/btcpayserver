using System;

namespace BTCPayServer.Abstractions.Contracts;

public interface IStoredFile
{
    string Id { get; set; }
    string FileName { get; set; }
    string StorageFileName { get; set; }
    DateTime Timestamp { get; set; }
    string ApplicationUserId { get; set; }
}
