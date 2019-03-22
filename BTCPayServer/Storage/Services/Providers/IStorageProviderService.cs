using System.Threading.Tasks;
using BTCPayServer.Storage.Models;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Storage.Services.Providers
{
    public interface IStorageProviderService
    {
        Task<StoredFile> AddFile(IFormFile formFile, StorageSettings configuration);
        Task RemoveFile(StoredFile storedFile, StorageSettings configuration);
        Task<string> GetFileBase64(StoredFile storedFile, StorageSettings configuration);
        Task<string> GetFileUrl(StoredFile storedFile, StorageSettings configuration);
        StorageProvider StorageProvider();
    }
}
