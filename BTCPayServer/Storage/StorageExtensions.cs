using BTCPayServer.Storage.Services;
using BTCPayServer.Storage.Services.Providers;
using BTCPayServer.Storage.Services.Providers.AmazonS3Storage;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage;
using BTCPayServer.Storage.Services.Providers.GoogleCloudStorage;
using Microsoft.Extensions.DependencyInjection;

namespace BTCPayServer.Storage
{
    public static class StorageExtensions
    {

        public static void AddProviderStorage(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<StoredFileRepository>();
            serviceCollection.AddSingleton<FileService>();
            serviceCollection.AddSingleton<IStorageProviderService, AmazonS3FileProviderService>();
            serviceCollection.AddSingleton<IStorageProviderService, AzureBlobStorageFileProviderService>();
            serviceCollection.AddSingleton<IStorageProviderService, FileSystemFileProviderService>();
            serviceCollection.AddSingleton<IStorageProviderService, GoogleCloudStorageFileProviderService>();
        }
    }
}
