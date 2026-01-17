using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using Microsoft.AspNetCore.Hosting.Server;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;

namespace Microsoft.AspNetCore.Hosting
{
    public static class WebHostExtensions
    {
        public static T GetServerFeatures<T>(this IHost host) where T : class
        {
            var server = host.Services.GetRequiredService<IServer>();
            var features = server.Features;
            return features.Get<T>();
        }

        public static async Task StartWithTasksAsync(this IHost host, CancellationToken cancellationToken = default)
        {
            // Load all tasks from DI
            var startupTasks = host.Services.GetServices<IStartupTask>();

            // Execute all the tasks
            foreach (var startupTask in startupTasks)
            {
                await startupTask.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            // Start the tasks as normal
            await host.StartAsync(cancellationToken).ConfigureAwait(false);
        }

        private const long MAX_DEBUG_LOG_FILE_SIZE = 2000000; // If debug log is in use roll it every N MB.
        public static IHostBuilder ConfigureSerilog(this IHostBuilder builder, IConfiguration configuration)
        {
            builder.ConfigureLogging(logBuilder =>
            {
                var debugLogFile = BTCPayServerOptions.GetDebugLog(configuration);
                if (!string.IsNullOrEmpty(debugLogFile))
                {
                    Serilog.Log.Logger = new LoggerConfiguration()
                        .Enrich.FromLogContext()
                        .MinimumLevel.Is(BTCPayServerOptions.GetDebugLogLevel(configuration))
                        .WriteTo.File(debugLogFile, rollingInterval: RollingInterval.Day, fileSizeLimitBytes: MAX_DEBUG_LOG_FILE_SIZE,
                            rollOnFileSizeLimit: true, retainedFileCountLimit: 1)
                        .CreateLogger();
                    logBuilder.AddProvider(new Serilog.Extensions.Logging.SerilogLoggerProvider(Log.Logger));
                }
            });
            return builder;
        }
    }
}
