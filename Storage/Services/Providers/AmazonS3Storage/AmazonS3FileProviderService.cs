using System.Threading.Tasks;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.AmazonS3Storage.Configuration;
using TwentyTwenty.Storage;
using TwentyTwenty.Storage.Amazon;

namespace BTCPayServer.Storage.Services.Providers.AmazonS3Storage
{
    public class
        AmazonS3FileProviderService : BaseTwentyTwentyStorageFileProviderServiceBase<AmazonS3StorageConfiguration>
    {
        public override StorageProvider StorageProvider()
        {
            return Storage.Models.StorageProvider.AmazonS3;
        }

        protected override Task<IStorageProvider> GetStorageProvider(AmazonS3StorageConfiguration configuration)
        {
            return Task.FromResult<IStorageProvider>(new AmazonStorageProvider(configuration));
        }
    }
}
