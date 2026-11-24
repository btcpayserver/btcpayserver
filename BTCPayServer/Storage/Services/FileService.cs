#nullable enable
using System;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Abstractions.Models;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Localization;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Storage.Services
{
    public class FileService
    {
        public const string LocalStorageDirectoryName = "LocalStorage";

        private readonly StoredFileRepository _fileRepository;
        private readonly IOptions<DataDirectories> _dataDirectories;
        private IStringLocalizer StringLocalizer { get; }

        public FileService(StoredFileRepository fileRepository,
            IStringLocalizer stringLocalizer,
            IOptions<DataDirectories> dataDirectories)
        {
            _fileRepository = fileRepository;
            _dataDirectories = dataDirectories;
            StringLocalizer = stringLocalizer;
        }

        public async Task<UploadImageResultModel> UploadImage(IFormFile file, string userId, long maxFileSizeInBytes = 1_000_000)
        {
            var result = new UploadImageResultModel();

            if (file.Length > maxFileSizeInBytes)
            {
                result.Success = false;
                result.Response = StringLocalizer["The uploaded file should be less than {0}", $"{maxFileSizeInBytes / 1_000_000}MB"].Value;
                return result;
            }
            if (!file.ContentType.StartsWith("image/", StringComparison.InvariantCulture))
            {
                result.Success = false;
                result.Response = StringLocalizer["The uploaded file needs to be an image"].Value;
                return result;
            }
            var formFile = await file.Bufferize();
            if (!FileTypeDetector.IsPicture(formFile.Buffer, formFile.FileName))
            {
                result.Success = false;
                result.Response = StringLocalizer["The uploaded file needs to be an image"].Value;
                return result;
            }
            try
            {
                result.StoredFile = await AddFile(formFile, userId);
                result.Success = true;
                result.Response = StringLocalizer["Image uploaded successfully"].Value;
            }
            catch (Exception e)
            {
                result.Success = false;
                result.Response = StringLocalizer["Could not save image: {0}", e.Message].Value;
            }

            return result;
        }

        public async Task<IStoredFile> AddFile(IFormFile file, string userId)
        {
            if (!file.FileName.IsValidFileName())
                throw new InvalidOperationException("Invalid file name");

            var storedFile = new StoredFile
            {
                Timestamp = DateTime.UtcNow,
                FileName = file.FileName,
                StorageFileName = $"{Guid.NewGuid()}-{file.FileName.ToLowerInvariant()}",
                ApplicationUserId = userId
            };
            var path = GetFilePath(storedFile);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            await using (var output = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None, 4096, true))
            {
                await file.CopyToAsync(output);
            }
            await _fileRepository.AddFile(storedFile);
            return storedFile;
        }

        public async Task<string?> GetFileUrl(Uri baseUri, string fileId)
        {
            var storedFile = await _fileRepository.GetFile(fileId);
            if (storedFile is null)
                return null;

            var escapedPath = Uri.EscapeDataString(Path.GetFileName(GetFilePath(storedFile)));
            return new Uri(baseUri, $"{baseUri.AbsolutePath.TrimEnd('/')}/{LocalStorageDirectoryName}/{escapedPath}").AbsoluteUri;
        }

        public async Task RemoveFile(string fileId, string userId)
        {
            var storedFile = await _fileRepository.GetFile(fileId);
            if (storedFile != null && (string.IsNullOrEmpty(userId) ||
                storedFile.ApplicationUserId.Equals(userId, StringComparison.InvariantCultureIgnoreCase)))
            {
                var path = GetFilePath(storedFile);
                if (!File.Exists(path))
                    throw new FileNotFoundException("The stored file was not found.", path);
                File.Delete(path);
                await _fileRepository.RemoveFile(storedFile);
            }
        }

        private string GetFilePath(StoredFile storedFile)
        {
            if (!storedFile.StorageFileName.IsValidFileName())
                throw new InvalidOperationException("Invalid file name");
            var root = Path.GetFullPath(_dataDirectories.Value.StorageDir);
            var path = Path.GetFullPath(Path.Combine(root, storedFile.StorageFileName));
            var comparison = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
            if (!path.StartsWith(Path.TrimEndingDirectorySeparator(root) + Path.DirectorySeparatorChar, comparison))
                throw new InvalidOperationException("Invalid storage path");
            return path;
        }
    }
}
