using Newtonsoft.Json.Linq;

namespace BTCPayServer.Storage.Services.Providers.GoogleCloudStorage.Configuration
{
    public static class GoogleCloudStorageFileProviderServiceExtensions
    {
        public static GoogleCloudStorageConfiguration ParseGoogleCloudStorageConfiguration(this JObject jObject)
        {
            return jObject.ToObject<GoogleCloudStorageConfiguration>();
        }

        public static JObject ConvertConfiguration(this GoogleCloudStorageConfiguration configuration)
        {
            return JObject.FromObject(configuration);
        }
    }
}