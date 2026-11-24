#nullable enable
using System;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Models;
using Microsoft.AspNetCore.Http;

namespace BTCPayServer.Storage.Services;

#pragma warning disable CS0618 // Compatibility adapter for plugins using IFileService.
class LegacyFileService(FileService fileService) : IFileService
#pragma warning restore CS0618
{
    public Task<bool> IsAvailable() => Task.FromResult(true);

    public Task<IStoredFile> AddFile(IFormFile file, string userId) => fileService.AddFile(file, userId);

    public Task<string?> GetFileUrl(Uri baseUri, string fileId) => fileService.GetFileUrl(baseUri, fileId);

    public Task RemoveFile(string fileId, string userId) => fileService.RemoveFile(fileId, userId);

    public Task<UploadImageResultModel> UploadImage(IFormFile file, string userId, long maxFileSizeInBytes = 1_000_000) =>
        fileService.UploadImage(file, userId, maxFileSizeInBytes);
}
