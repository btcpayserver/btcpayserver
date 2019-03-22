using Newtonsoft.Json.Linq;

namespace BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration
{
    public static class FileSystemFileProviderServiceExtensions
    {
        public static FileSystemStorageConfiguration ParseFileSystemStorageConfiguration(this JObject jObject)
        {
            return jObject.ToObject<FileSystemStorageConfiguration>();
        }

        public static JObject ConvertConfiguration(this FileSystemStorageConfiguration configuration)
        {
            return JObject.FromObject(configuration);
        }
    }
}