using Newtonsoft.Json.Linq;

namespace BTCPayServer.Storage.Services.Providers.AzureBlobStorage.Configuration
{
    public static class AzureBlobStorageFileProviderServiceExtensions
    {
        public static AzureBlobStorageConfiguration ParseAzureBlobStorageConfiguration(this JObject jObject)
        {
            return jObject.ToObject<AzureBlobStorageConfiguration>();
        }

        public static JObject ConvertConfiguration(this AzureBlobStorageConfiguration configuration)
        {
            return JObject.FromObject(configuration);
        }
    }
}