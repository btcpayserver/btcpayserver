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
        private readonly IHostingEnvironment _HostingEnvironment;
        private readonly BTCPayServerEnvironment _BtcPayServerEnvironment;
        private readonly BTCPayServerOptions _Options;
        private readonly IHttpContextAccessor _HttpContextAccessor;

        public FileSystemFileProviderService(IHostingEnvironment hostingEnvironment,
            BTCPayServerEnvironment btcPayServerEnvironment, BTCPayServerOptions options, IHttpContextAccessor httpContextAccessor)
        {
            _HostingEnvironment = hostingEnvironment;
            _BtcPayServerEnvironment = btcPayServerEnvironment;
            _Options = options;
            _HttpContextAccessor = httpContextAccessor;
        }

        public override StorageProvider StorageProvider()
        {
            return Storage.Models.StorageProvider.FileSystem;
        }

        public override FileSystemStorageConfiguration GetProviderConfiguration(StorageSettings configuration)
        {
            return configuration.Configuration.ParseFileSystemStorageConfiguration();
        }

        protected override Task<IStorageProvider> GetStorageProvider(FileSystemStorageConfiguration configuration)
        {
            return Task.FromResult<IStorageProvider>(
                new LocalStorageProvider(Path.Combine(_HostingEnvironment.WebRootPath, configuration.BasePath)));
        }

        public override async Task<string> GetFileUrl(StoredFile storedFile, StorageSettings configuration)
        {
            
            var baseResult = await base.GetFileUrl(storedFile, configuration);
            var url =
                _HttpContextAccessor.HttpContext.Request.IsOnion()?
                    _BtcPayServerEnvironment.OnionUrl :
                
                $"{_BtcPayServerEnvironment.ExpectedProtocol}://" +
                $"{_BtcPayServerEnvironment.ExpectedHost}" +
                $"{(string.IsNullOrEmpty(_Options.RootPath) ? "" : "/" + _Options.RootPath)}";
            return baseResult.Replace(_HostingEnvironment.WebRootPath, url,
                StringComparison.InvariantCultureIgnoreCase);
        }
    }
}
