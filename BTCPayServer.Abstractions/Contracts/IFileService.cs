#nullable enable
using System;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Abstractions.Contracts;

public interface IFileService
{
    Task<bool> IsAvailable();
    Task<IStoredFile> AddFile(IFormFile file, string userId);
    Task<IStoredFile> AddFile(Uri file, string userId);
    Task<string?> GetFileUrl(Uri baseUri, string fileId);
    Task<string?> GetTemporaryFileUrl(Uri baseUri, string fileId, DateTimeOffset expiry,
        bool isDownload);
    Task RemoveFile(string fileId, string userId);
}
