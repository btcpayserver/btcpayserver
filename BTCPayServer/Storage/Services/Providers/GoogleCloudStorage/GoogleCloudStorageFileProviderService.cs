using System.Threading.Tasks;
using BTCPayServer.Storage.Models;
using BTCPayServer.Storage.Services.Providers.GoogleCloudStorage.Configuration;
using Google.Apis.Auth.OAuth2;
using TwentyTwenty.Storage;
using TwentyTwenty.Storage.Google;

namespace BTCPayServer.Storage.Services.Providers.GoogleCloudStorage
{
    public class
        GoogleCloudStorageFileProviderService : BaseTwentyTwentyStorageFileProviderServiceBase<
            GoogleCloudStorageConfiguration>
    {
        public override StorageProvider StorageProvider()
        {
            return Storage.Models.StorageProvider.GoogleCloudStorage;
        }

        protected override Task<IStorageProvider> GetStorageProvider(
            GoogleCloudStorageConfiguration configuration)
        {
            return Task.FromResult<IStorageProvider>(new GoogleStorageProvider(GoogleCredential.FromJson(configuration.JsonCredentials), configuration));
        }
    }
}
