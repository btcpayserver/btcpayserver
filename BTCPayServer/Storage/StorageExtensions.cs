using System;
using System.IO;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Storage.Services;
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
        public static void AddFileStorage(this IServiceCollection serviceCollection)
        {
            serviceCollection.AddSingleton<StoredFileRepository>();
            serviceCollection.AddSingleton<FileService>();
#pragma warning disable CS0618 // Keep legacy plugin injection working; application code uses FileService.
            serviceCollection.AddSingleton<IFileService, LegacyFileService>();
#pragma warning restore CS0618
        }

        public static void UseFileStorage(this IApplicationBuilder builder, IOptions<DataDirectories> datadirs)
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
                    RequestPath = new PathString($"/{FileService.LocalStorageDirectoryName}"),
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
