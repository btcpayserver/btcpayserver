using System.IO;
using BTCPayServer.Configuration;
using BTCPayServer.Storage.Services;
using BTCPayServer.Storage.Services.Providers;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage;
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
//            serviceCollection.AddSingleton<IStorageProviderService, AmazonS3FileProviderService>();
            serviceCollection.AddSingleton<IStorageProviderService, AzureBlobStorageFileProviderService>();
            serviceCollection.AddSingleton<IStorageProviderService, FileSystemFileProviderService>();
//            serviceCollection.AddSingleton<IStorageProviderService, GoogleCloudStorageFileProviderService>();
        }

        public static void UseProviderStorage(this IApplicationBuilder builder, BTCPayServerOptions options)
        {
            var dir = FileSystemFileProviderService.GetStorageDir(options);
            var tmpdir = FileSystemFileProviderService.GetTempStorageDir(options);
            DirectoryInfo dirInfo;
            if (!Directory.Exists(dir))
            {
                dirInfo = Directory.CreateDirectory(dir);
            } 
            else
            {
                dirInfo = new DirectoryInfo(dir);
            }

            DirectoryInfo tmpdirInfo;
            if (!Directory.Exists(tmpdir))
            {
                tmpdirInfo = Directory.CreateDirectory(tmpdir);
            } 
            else
            {
                tmpdirInfo = new DirectoryInfo(tmpdir);
            }

            builder.UseStaticFiles(new StaticFileOptions()
            {
                ServeUnknownFileTypes = true,
                RequestPath = new PathString($"/{FileSystemFileProviderService.LocalStorageDirectoryName}"),
                FileProvider = new PhysicalFileProvider(dirInfo.FullName),
                OnPrepareResponse = context =>
                {
                    if (context.Context.Request.Query.ContainsKey("download"))
                    {
                        context.Context.Response.Headers["Content-Disposition"] = "attachment";
                    }
                }
            });
            builder.UseStaticFiles(new StaticFileOptions()
            {
                ServeUnknownFileTypes = true,
                RequestPath = new PathString($"/{FileSystemFileProviderService.LocalStorageDirectoryName}tmp"),
                FileProvider = new TemporaryLocalFileProvider(tmpdirInfo, dirInfo, builder.ApplicationServices.GetService<StoredFileRepository>()),
                OnPrepareResponse = context =>
                {
                    if (context.Context.Request.Query.ContainsKey("download"))
                    {
                        context.Context.Response.Headers["Content-Disposition"] = "attachment";
                    }
                }
            });
            
        }
    }
}
