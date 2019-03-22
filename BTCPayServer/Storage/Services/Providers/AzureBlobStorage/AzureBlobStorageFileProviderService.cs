using System.Threading.Tasks;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration;
using TwentyTwenty.Storage;
using TwentyTwenty.Storage.Azure;

namespace BTCPayServer.Storage.Services.Providers.AzureBlobStorage
{
    public class
        AzureBlobStorageFileProviderService : BaseTwentyTwentyStorageFileProviderServiceBase<
            AzureBlobStorageConfiguration>
    {
        public override StorageProvider StorageProvider()
        {
            return Storage.Models.StorageProvider.AzureBlobStorage;
        }

        public override AzureBlobStorageConfiguration GetProviderConfiguration(StorageSettings configuration)
        {
            return configuration.Configuration.ParseAzureBlobStorageConfiguration();
        }

        protected override Task<IStorageProvider> GetStorageProvider(AzureBlobStorageConfiguration configuration)
        {
            return Task.FromResult<IStorageProvider>(new AzureStorageProvider(configuration));
        }
    }
}
