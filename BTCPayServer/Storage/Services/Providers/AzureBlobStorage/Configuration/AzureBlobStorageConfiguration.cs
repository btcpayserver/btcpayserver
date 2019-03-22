using BTCPayServer.Storage.Services.Providers.Models;
using TwentyTwenty.Storage.Azure;

namespace BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration
{
    public class AzureBlobStorageConfiguration : AzureProviderOptions, IBaseStorageConfiguration
    {
        public string ContainerName { get; set; }
    }
}