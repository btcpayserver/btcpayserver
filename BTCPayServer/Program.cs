using System;
using System.IO;
using System.Linq;
using System.Net;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Logging;
using BTCPayServer.Plugins;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("BTCPayServer.Tests")]

namespace BTCPayServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            if (args.Length > 0 && args[0] == "run")
                args = args.Skip(1).ToArray(); // Hack to make dotnet watch work

            ServicePointManager.DefaultConnectionLimit = 100;
            IWebHost host = null;
            var processor = new ConsoleLoggerProcessor();
            var loggerProvider = new CustomConsoleLogProvider(processor);
            using var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var logger = loggerFactory.CreateLogger("Configuration");
            var logs = new Logs();
            logs.Configure(loggerFactory);
            IConfiguration conf = null;
            try
            {
                var confBuilder = new DefaultConfiguration() { Logger = logger }.CreateConfigurationBuilder(args);
                if (confBuilder is null)
                    return;
#if DEBUG
                confBuilder.AddJsonFile("appsettings.dev.json", true, false);
#endif
                conf = confBuilder.Build();
                var builder = new WebHostBuilder()
                    .UseKestrel()
                    .UseConfiguration(conf)
                    .ConfigureLogging(l =>
                    {
                        l.AddFilter("Microsoft", LogLevel.Error);
                        if (!conf.GetOrDefault<bool>("verbose", false))
                            l.AddFilter("Events", LogLevel.Warning);
                        // Uncomment this to see EF queries
                        //l.AddFilter("Microsoft.EntityFrameworkCore.Database.Command", LogLevel.Trace);
                        l.AddFilter("Microsoft.EntityFrameworkCore.Migrations", LogLevel.Information);
                        l.AddFilter("System.Net.Http.HttpClient", LogLevel.Critical);
                        l.AddFilter("Microsoft.AspNetCore.Antiforgery.Internal", LogLevel.Critical);
                        l.AddFilter("Fido2NetLib.DistributedCacheMetadataService", LogLevel.Error);
                        l.AddProvider(new CustomConsoleLogProvider(processor));
                    })
                    .UseStartup<Startup>();

                // When we run the app with dotnet run (typically in dev env), the wwwroot isn't in the same directory
                // than this assembly.
                // But when we use dotnet publish, the wwwroot is published alongside the assembly!
                // This fix https://github.com/btcpayserver/btcpayserver/issues/1894
                var defaultContentPath = Path.GetDirectoryName(Assembly.GetExecutingAssembly().Location);
                var defaultWebRoot = Path.Combine(defaultContentPath, "wwwroot");
                var defaultWebRootExists = Directory.Exists(defaultWebRoot);
                if (!defaultWebRootExists)
                {
                    // When we use dotnet run...
                    builder.UseContentRoot(Directory.GetCurrentDirectory());
                }
                host = builder.Build();
                await host.StartWithTasksAsync();
                var urls = host.ServerFeatures.Get<IServerAddressesFeature>().Addresses;
                foreach (var url in urls)
                {
                    // Some tools such as dotnet watch parse this exact log to open the browser
                    logger.LogInformation("Now listening on: " + url);
                }
                await host.WaitForShutdownAsync();
            }
            catch (ConfigException ex)
            {
                if (!string.IsNullOrEmpty(ex.Message))
                    logs.Configuration.LogError(ex.Message);
            }
            catch (Exception e) when (PluginManager.IsExceptionByPlugin(e, out var pluginName))
            {
                logs.Configuration.LogError(e, $"Plugin crash during startup detected, disabling {pluginName}...");
                var pluginDir = new DataDirectories().Configure(conf).PluginDir;
                PluginManager.DisablePlugin(pluginDir, pluginName);
            }
            finally
            {
                processor.Dispose();
                if (host == null)
                    logs.Configuration.LogError("Configuration error");
                host?.Dispose();
                await Serilog.Log.CloseAndFlushAsync();
                loggerProvider.Dispose();
            }
        }
    }
}
