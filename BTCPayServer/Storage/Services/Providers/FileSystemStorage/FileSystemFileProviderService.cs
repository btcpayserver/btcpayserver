using System;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Data;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration;
using ExchangeSharp;
using Microsoft.AspNetCore.Hosting;
using Newtonsoft.Json;
using TwentyTwenty.Storage;
using TwentyTwenty.Storage.Local;

namespace BTCPayServer.Storage.Services.Providers.FileSystemStorage
{
    public class
        FileSystemFileProviderService : BaseTwentyTwentyStorageFileProviderServiceBase<FileSystemStorageConfiguration>
    {
        private readonly BTCPayServerOptions _options;

        public FileSystemFileProviderService(BTCPayServerOptions options)
        {
            _options = options;
        }
        public const string LocalStorageDirectoryName = "LocalStorage";

        public static string GetStorageDir(BTCPayServerOptions options)
        {
            return Path.Combine(options.DataDir, LocalStorageDirectoryName);
        }
        
        
        public static string GetTempStorageDir(BTCPayServerOptions options)
        {
            return Path.Combine(GetStorageDir(options), "tmp");
        }
        public override StorageProvider StorageProvider()
        {
            return Storage.Models.StorageProvider.FileSystem;
        }

        protected override Task<IStorageProvider> GetStorageProvider(FileSystemStorageConfiguration configuration)
        {
            return Task.FromResult<IStorageProvider>(
                new LocalStorageProvider(new DirectoryInfo(GetStorageDir(_options)).FullName));
        }

        public override async Task<string> GetFileUrl(Uri baseUri, StoredFile storedFile, StorageSettings configuration)
        {
            var baseResult = await base.GetFileUrl(baseUri, storedFile, configuration);
            var url = new Uri(baseUri,LocalStorageDirectoryName );
            return baseResult.Replace(new DirectoryInfo(GetStorageDir(_options)).FullName, url.AbsoluteUri,
                StringComparison.InvariantCultureIgnoreCase);
        }

        public override async Task<string> GetTemporaryFileUrl(Uri baseUri, StoredFile storedFile,
            StorageSettings configuration, DateTimeOffset expiry, bool isDownload,
            BlobUrlAccess access = BlobUrlAccess.Read)
        {

            var localFileDescriptor = new TemporaryLocalFileDescriptor()
            {
                Expiry = expiry, 
                FileId = storedFile.Id, 
                IsDownload = isDownload
            };
            var name = Guid.NewGuid().ToString();
            var fullPath = Path.Combine(GetTempStorageDir(_options), name);
            if (!File.Exists(fullPath))
            {
                File.Create(fullPath).Dispose();
            }
            
            await File.WriteAllTextAsync(Path.Combine(GetTempStorageDir(_options), name), JsonConvert.SerializeObject(localFileDescriptor));
            
            return  new Uri(baseUri,$"{LocalStorageDirectoryName}tmp/{name}{(isDownload ? "?download" : string.Empty)}").AbsoluteUri;
        }
    }
}
