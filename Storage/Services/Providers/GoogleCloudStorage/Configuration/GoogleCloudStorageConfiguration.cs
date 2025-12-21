using BTCPayServer.Storage.Services.Providers.Models;
using TwentyTwenty.Storage.Google;

namespace BTCPayServer.Storage.Services.Providers.GoogleCloudStorage.Configuration
{
    public class GoogleCloudStorageConfiguration : GoogleProviderOptions, IBaseStorageConfiguration
    {
        public string JsonCredentials { get; set; }
        public string ContainerName { get; set; }
    }
}
