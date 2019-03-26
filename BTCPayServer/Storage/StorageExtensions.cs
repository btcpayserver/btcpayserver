using System.IO;
using BTCPayServer.Configuration;
using BTCPayServer.Storage.Services;
using BTCPayServer.Storage.Services.Providers;
using BTCPayServer.Storage.Services.Providers.AmazonS3Storage;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage;
using BTCPayServer.Storage.Services.Providers.GoogleCloudStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;

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

        public static void UseProviderStorage(this IApplicationBuilder builder, BTCPayServerOptions options)
        {
            var dir = FileSystemFileProviderService.GetStorageDir(options);
            if (!Directory.Exists(dir))
            {
                Directory.CreateDirectory(dir);
            }
            builder.UseStaticFiles(new StaticFileOptions()
            {
                ServeUnknownFileTypes = true,
                RequestPath = new PathString("/Storage"),
                FileProvider = new PhysicalFileProvider(dir)
            });
        }
    }
}
