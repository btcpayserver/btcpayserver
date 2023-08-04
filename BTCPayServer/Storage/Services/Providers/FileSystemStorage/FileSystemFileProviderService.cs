using System;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration;
using Microsoft.Extensions.Options;
using Newtonsoft.Json;
using TwentyTwenty.Storage;
using TwentyTwenty.Storage.Local;

namespace BTCPayServer.Storage.Services.Providers.FileSystemStorage
{
    public class
        FileSystemFileProviderService : BaseTwentyTwentyStorageFileProviderServiceBase<FileSystemStorageConfiguration>
    {
        private readonly IOptions<DataDirectories> _datadirs;

        public FileSystemFileProviderService(IOptions<DataDirectories> datadirs)
        {
            _datadirs = datadirs;
        }
        public const string LocalStorageDirectoryName = "LocalStorage";

        public override StorageProvider StorageProvider()
        {
            return Storage.Models.StorageProvider.FileSystem;
        }

        protected override Task<IStorageProvider> GetStorageProvider(FileSystemStorageConfiguration configuration)
        {
            return Task.FromResult<IStorageProvider>(
                new LocalStorageProvider(new DirectoryInfo(_datadirs.Value.StorageDir).FullName));
        }

        public override async Task<string> GetFileUrl(Uri baseUri, StoredFile storedFile, StorageSettings configuration)
        {
            var baseResult = await base.GetFileUrl(baseUri, storedFile, configuration);
            // Set the relative URL to the directory name if the root path is default, otherwise add root path before the directory name
            var relativeUrl = baseUri.AbsolutePath == "/" ? LocalStorageDirectoryName : $"{baseUri.AbsolutePath}/{LocalStorageDirectoryName}";
            var url = new Uri(baseUri, relativeUrl);
            var r = baseResult.Replace(new DirectoryInfo(_datadirs.Value.StorageDir).FullName, url.AbsoluteUri,
                StringComparison.InvariantCultureIgnoreCase);
            if (Path.DirectorySeparatorChar == '\\')
                r = r.Replace(Path.DirectorySeparatorChar, '/');
            return r;
        }

        public override async Task<string> GetTemporaryFileUrl(Uri baseUri, StoredFile storedFile,
            StorageSettings configuration, DateTimeOffset expiry, bool isDownload,
            BlobUrlAccess access = BlobUrlAccess.Read)
        {

            var localFileDescriptor = new TemporaryLocalFileDescriptor
            {
                Expiry = expiry,
                FileId = storedFile.Id,
                IsDownload = isDownload
            };
            var name = Guid.NewGuid().ToString();
            var fullPath = Path.Combine(_datadirs.Value.TempStorageDir, name);
            var fileInfo = new FileInfo(fullPath);
            if (!fileInfo.Exists)
            {
                fileInfo.Directory?.Create();
                await File.Create(fileInfo.FullName).DisposeAsync();
            }

            await File.WriteAllTextAsync(Path.Combine(_datadirs.Value.TempStorageDir, name), JsonConvert.SerializeObject(localFileDescriptor));

            return new Uri(baseUri, $"{LocalStorageDirectoryName}tmp/{name}{(isDownload ? "?download" : string.Empty)}").AbsoluteUri;
        }
    }
}
