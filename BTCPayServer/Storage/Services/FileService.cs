#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using System.Web;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Configuration;
using BTCPayServer.Services;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Storage.Services
{
    public class FileService : IFileService
    {
        private readonly StoredFileRepository _fileRepository;
        private readonly IEnumerable<IStorageProviderService> _providers;
        private readonly SettingsRepository _settingsRepository;
        private readonly IOptions<DataDirectories> _dataDirectories;
        private readonly IHttpClientFactory _httpClientFactory;

        public FileService(StoredFileRepository fileRepository,
            SettingsRepository settingsRepository,
            IEnumerable<IStorageProviderService> providers,
            IHttpClientFactory httpClientFactory,
            IOptions<DataDirectories> dataDirectories)
        {
            _fileRepository = fileRepository;
            _providers = providers;
            _settingsRepository = settingsRepository;
            _httpClientFactory = httpClientFactory;
            _dataDirectories = dataDirectories;
        }

        public async Task<bool> IsAvailable()
        {
            var settings = await _settingsRepository.GetSettingAsync<StorageSettings>();
            return settings is not null;
        }

        public async Task<IStoredFile> AddFile(IFormFile file, string userId)
        {
            var settings = await _settingsRepository.GetSettingAsync<StorageSettings>();
            if (settings is null)
                throw new InvalidOperationException("StoreSettings not configured");
            if (!file.FileName.IsValidFileName())
                throw new InvalidOperationException("Invalid file name");

            var provider = GetProvider(settings);
            var storedFile = await provider.AddFile(file, settings);
            storedFile.ApplicationUserId = userId;
            await _fileRepository.AddFile(storedFile);
            return storedFile;
        }

        public async Task<IStoredFile> AddFile(Uri url, string userId)
        {
            if (!await IsAvailable())
                throw new InvalidOperationException("StoreSettings not configured");

            var fileName = Sanitize(Path.GetFileName(url.AbsolutePath));
            if (!fileName.IsValidFileName())
                throw new InvalidOperationException("Invalid file name");

            // download
            var filePath = Path.Join(_dataDirectories.Value.TempDir, fileName);
            var httClient = _httpClientFactory.CreateClient();
            using var resp = await httClient.GetAsync(url);
            await using var stream = new FileStream(filePath, FileMode.Create, FileAccess.ReadWrite);
            await resp.Content.CopyToAsync(stream);
            var file = new FormFile(stream, 0, stream.Length, fileName, fileName)
            {
                Headers = new HeaderDictionary(),
                ContentType = GetContentType(filePath)
            };
            await stream.FlushAsync();

            var storedFile = await AddFile(file, userId);

            // cleanup
            File.Delete(filePath);

            return storedFile;
        }

        public async Task<string?> GetFileUrl(Uri baseUri, string fileId)
        {
            var settings = await _settingsRepository.GetSettingAsync<StorageSettings>();
            if (settings is null)
                return null;
            var provider = GetProvider(settings);
            var storedFile = await _fileRepository.GetFile(fileId);
            return storedFile == null ? null : await provider.GetFileUrl(baseUri, storedFile, settings);
        }

        public async Task<string?> GetTemporaryFileUrl(Uri baseUri, string fileId, DateTimeOffset expiry,
            bool isDownload)
        {
            var settings = await _settingsRepository.GetSettingAsync<StorageSettings>();
            if (settings is null)
                return null;
            var provider = GetProvider(settings);
            var storedFile = await _fileRepository.GetFile(fileId);
            return storedFile == null ? null : await provider.GetTemporaryFileUrl(baseUri, storedFile, settings, expiry, isDownload);
        }

        public async Task RemoveFile(string fileId, string userId)
        {
            var settings = await _settingsRepository.GetSettingAsync<StorageSettings>();
            if (settings is null)
                return;
            var provider = GetProvider(settings);
            var storedFile = await _fileRepository.GetFile(fileId);
            if (storedFile != null && (string.IsNullOrEmpty(userId) ||
                storedFile.ApplicationUserId.Equals(userId, StringComparison.InvariantCultureIgnoreCase)))
            {
                await provider.RemoveFile(storedFile, settings);
                await _fileRepository.RemoveFile(storedFile);
            }
        }

        private IStorageProviderService GetProvider(StorageSettings storageSettings)
        {
            return _providers.First((service) => service.StorageProvider().Equals(storageSettings.Provider));
        }

        private static string GetContentType(string filePath)
        {
            var mimeProvider = new FileExtensionContentTypeProvider();
            if (!mimeProvider.TryGetContentType(filePath, out string? contentType))
            {
                contentType = "application/octet-stream";
            }

            return contentType;
        }

        private static string Sanitize(string fileName)
        {
            var invalid = Path.GetInvalidFileNameChars().Concat(":").ToArray();
            // replace invalid chars in url-decoded filename
            var name = string.Join("_", HttpUtility.UrlDecode(fileName)
                .Split(invalid, StringSplitOptions.RemoveEmptyEntries)).TrimEnd('.');
            // replace multiple underscores with just one
            return Regex.Replace(name, @"_+", "_");
        }
    }
}
