using System;
using System.Threading.Tasks;
using BTCPayServer.Storage.Models;
using Microsoft.AspNetCore.Http;
using TwentyTwenty.Storage;

namespace BTCPayServer.Storage.Services.Providers
{
    public interface IStorageProviderService
    {
        Task<StoredFile> AddFile(IFormFile formFile, StorageSettings configuration);
        Task RemoveFile(StoredFile storedFile, StorageSettings configuration);
        Task<string> GetFileUrl(StoredFile storedFile, StorageSettings configuration);
        Task<string> GetTemporaryFileUrl(StoredFile storedFile, StorageSettings configuration,
            DateTimeOffset expiry, bool isDownload, BlobUrlAccess access = BlobUrlAccess.Read);
        StorageProvider StorageProvider();
    }
}
