#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Abstractions.Contracts;

[Obsolete("Inject FileService instead.")]
public interface IFileService
{
    Task<bool> IsAvailable();
    Task<IStoredFile> AddFile(IFormFile file, string userId);
    Task<string?> GetFileUrl(Uri baseUri, string fileId);
    /// <summary>
    /// Removes a file owned by the specified user. The user ID must not be null or empty.
    /// For server-admin deletion without an ownership check, inject FileService instead.
    /// </summary>
    Task RemoveFile(string fileId, string userId);
    Task<UploadImageResultModel> UploadImage(IFormFile file, string userId, long maxFileSizeInBytes = 1_000_000);
}
