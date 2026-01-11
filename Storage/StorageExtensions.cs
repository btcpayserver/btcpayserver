using System;
using System.IO;
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
            serviceCollection.AddSingleton<IStorageProviderService, AzureBlobStorageFileProviderService>();
            serviceCollection.AddSingleton<IStorageProviderService, FileSystemFileProviderService>();
        }

        public static void UseProviderStorage(this IApplicationBuilder builder, IOptions<DataDirectories> datadirs)
        {
            try
            {
                var dirInfo = Directory.Exists(datadirs.Value.StorageDir)
                    ? new DirectoryInfo(datadirs.Value.StorageDir)
                    : Directory.CreateDirectory(datadirs.Value.StorageDir);

                if (!Directory.Exists(datadirs.Value.TempDir))
                {
                    Directory.CreateDirectory(datadirs.Value.TempDir);
                }

                builder.UseStaticFiles(new StaticFileOptions
                {
                    ServeUnknownFileTypes = true,
                    RequestPath = new PathString($"/{FileSystemFileProviderService.LocalStorageDirectoryName}"),
                    FileProvider = new PhysicalFileProvider(dirInfo.FullName),
                    OnPrepareResponse = HandleStaticFileResponse()
                });
            }
            catch (Exception e)
            {
                Logs.Utils.LogError(e, "Could not initialize the Local File Storage system (for uploading and storing files locally)");
            }
        }

        private static Action<StaticFileResponseContext> HandleStaticFileResponse()
        {
            return context =>
            {
                context.Context.Response.Headers["Content-Disposition"] = context.Context.Request.Query.ContainsKey("download")? "attachment" : "inline";
                context.Context.Response.Headers["Content-Security-Policy"] = "script-src ;";
                context.Context.Response.Headers["X-Content-Type-Options"] = "nosniff";
            };
        }
    }
}
