using System;
using System.IO;
using System.Runtime.InteropServices.ComTypes;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Storage.Services;
using BTCPayServer.Storage.Services.Providers;
using BTCPayServer.Storage.Services.Providers.AzureBlobStorage;
using BTCPayServer.Storage.Services.Providers.FileSystemStorage;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.StaticFiles;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using NBitcoin.Logging;

namespace BTCPayServer.Storage
{
    public static class StorageExtensions
    {
        public static void AddProviderStorage(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<StoredFileRepository>();
            serviceCollection.AddSingleton<FileService>();
            serviceCollection.AddSingleton<IFileService>(provider => provider.GetRequiredService<FileService>());
            //            serviceCollection.AddSingleton<IStorageProviderService, AmazonS3FileProviderService>();
            serviceCollection.AddSingleton<IStorageProviderService, AzureBlobStorageFileProviderService>();
            serviceCollection.AddSingleton<IStorageProviderService, FileSystemFileProviderService>();
            //            serviceCollection.AddSingleton<IStorageProviderService, GoogleCloudStorageFileProviderService>();
        }

        public static void UseProviderStorage(this IApplicationBuilder builder, IOptions<DataDirectories> datadirs)
        {
            try
            {
                DirectoryInfo dirInfo;
                if (!Directory.Exists(datadirs.Value.StorageDir))
                {
                    dirInfo = Directory.CreateDirectory(datadirs.Value.StorageDir);
                }
                else
                {
                    dirInfo = new DirectoryInfo(datadirs.Value.StorageDir);
                }
                
                if (!Directory.Exists(datadirs.Value.TempDir))
                {
                    Directory.CreateDirectory(datadirs.Value.TempDir);
                }

                DirectoryInfo tmpdirInfo;
                if (!Directory.Exists(datadirs.Value.TempStorageDir))
                {
                    tmpdirInfo = Directory.CreateDirectory(datadirs.Value.TempStorageDir);
                }
                else
                {
                    tmpdirInfo = new DirectoryInfo(datadirs.Value.TempStorageDir);
                }

                builder.UseStaticFiles(new StaticFileOptions()
                {
                    ServeUnknownFileTypes = true,
                    RequestPath = new PathString($"/{FileSystemFileProviderService.LocalStorageDirectoryName}"),
                    FileProvider = new PhysicalFileProvider(dirInfo.FullName),
                    OnPrepareResponse = HandleStaticFileResponse()
                });
                builder.UseStaticFiles(new StaticFileOptions()
                {
                    ServeUnknownFileTypes = true,
                    RequestPath = new PathString($"/{FileSystemFileProviderService.LocalStorageDirectoryName}tmp"),
                    FileProvider = new TemporaryLocalFileProvider(tmpdirInfo, dirInfo,
                        builder.ApplicationServices.GetService<StoredFileRepository>()),
                    OnPrepareResponse = HandleStaticFileResponse()
                });
            }
            catch (Exception e)
            {
                Logs.Utils.LogError(e, $"Could not initialize the Local File Storage system(uploading and storing files locally)");
            }
        }

        private static Action<StaticFileResponseContext> HandleStaticFileResponse()
        {
            return context =>
            {
                if (context.Context.Request.Query.ContainsKey("download"))
                {
                    context.Context.Response.Headers["Content-Disposition"] = "attachment";
                }
            };
        }
    }
}
