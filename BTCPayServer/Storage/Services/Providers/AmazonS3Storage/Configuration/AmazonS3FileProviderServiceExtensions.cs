using Newtonsoft.Json.Linq;

namespace BTCPayServer.Storage.Services.Providers.AmazonS3Storage.Configuration
{
    public static class AmazonS3FileProviderServiceExtensions
    {
        public static AmazonS3StorageConfiguration ParseAmazonS3StorageConfiguration(this JObject jObject)
        {
            return jObject.ToObject<AmazonS3StorageConfiguration>();
        }

        public static JObject ConvertConfiguration(this AmazonS3StorageConfiguration configuration)
        {
            return JObject.FromObject(configuration);
        }
    }
}