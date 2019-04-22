using System;
using System.IO;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Services;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using TwentyTwenty.Storage;
using TwentyTwenty.Storage.Local;

namespace BTCPayServer.Storage.Services.Providers.FileSystemStorage
{
    public class
        FileSystemFileProviderService : BaseTwentyTwentyStorageFileProviderServiceBase<FileSystemStorageConfiguration>
    {
        private readonly BTCPayServerEnvironment _BtcPayServerEnvironment;
        private readonly BTCPayServerOptions _Options;
        private readonly IHttpContextAccessor _HttpContextAccessor;

        public FileSystemFileProviderService(BTCPayServerEnvironment btcPayServerEnvironment,
            BTCPayServerOptions options, IHttpContextAccessor httpContextAccessor)
        {
            _BtcPayServerEnvironment = btcPayServerEnvironment;
            _Options = options;
            _HttpContextAccessor = httpContextAccessor;
        }
        public const string LocalStorageDirectoryName = "LocalStorage";

        public static string GetStorageDir(BTCPayServerOptions options)
        {
            return Path.Combine(options.DataDir, LocalStorageDirectoryName);
        }

        public override StorageProvider StorageProvider()
        {
            return Storage.Models.StorageProvider.FileSystem;
        }

        protected override Task<IStorageProvider> GetStorageProvider(FileSystemStorageConfiguration configuration)
        {
            return Task.FromResult<IStorageProvider>(
                new LocalStorageProvider(new DirectoryInfo(GetStorageDir(_Options)).FullName));
        }

        public override async Task<string> GetFileUrl(StoredFile storedFile, StorageSettings configuration)
        {
            var baseResult = await base.GetFileUrl(storedFile, configuration);
            var url =
                _HttpContextAccessor.HttpContext.Request.IsOnion()
                    ? _BtcPayServerEnvironment.OnionUrl
                    : $"{_BtcPayServerEnvironment.ExpectedProtocol}://" +
                      $"{_BtcPayServerEnvironment.ExpectedHost}" +
                      $"{_Options.RootPath}{LocalStorageDirectoryName}";
            return baseResult.Replace(new DirectoryInfo(GetStorageDir(_Options)).FullName, url,
                StringComparison.InvariantCultureIgnoreCase);
        }

        public override Task<string> GetTemporaryFileUrl(StoredFile storedFile, StorageSettings configuration, DateTimeOffset expiry, bool isDownload,
            BlobUrlAccess access = BlobUrlAccess.Read)
        {
            return GetFileUrl(storedFile, configuration);
        }
    }
}
