using BTCPayServer.Storage.Services.Providers.Models;

namespace BTCPayServer.Storage.Services.Providers.FileSystemStorage.Configuration
{
    public class FileSystemStorageConfiguration : IBaseStorageConfiguration
    {
        public string ContainerName { get; set; } = string.Empty;
    }
}
