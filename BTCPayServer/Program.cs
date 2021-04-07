using System;
using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
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
        static void Main(string[] args)
        {
            ServicePointManager.DefaultConnectionLimit = 100;
            IWebHost host = null;
            var processor = new ConsoleLoggerProcessor();
            CustomConsoleLogProvider loggerProvider = new CustomConsoleLogProvider(processor);
            using var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var logger = loggerFactory.CreateLogger("Configuration");
            IConfiguration conf = null;
            try
            {
                // This is the only way that LoadArgs can print to console. Because LoadArgs is called by the HostBuilder before Logs.Configure is called
                conf = new DefaultConfiguration() { Logger = logger }.CreateConfiguration(args);
                if (conf == null)
                    return;
                Logs.Configure(loggerFactory);
                new BTCPayServerOptions().LoadArgs(conf);
                Logs.Configure(null);
                /////

                host = new WebHostBuilder()
                    .UseKestrel()
                    .UseIISIntegration()
                    .UseContentRoot(Directory.GetCurrentDirectory())
                    .UseConfiguration(conf)
                    .ConfigureLogging(l =>
                    {
                        l.AddFilter("Microsoft", LogLevel.Error);
                        l.AddFilter("System.Net.Http.HttpClient", LogLevel.Critical);
                        l.AddFilter("Microsoft.AspNetCore.Antiforgery.Internal", LogLevel.Critical);
                        l.AddProvider(new CustomConsoleLogProvider(processor));
                    })
                    .UseStartup<Startup>()
                    .Build();
                host.StartWithTasksAsync().GetAwaiter().GetResult();
                var urls = host.ServerFeatures.Get<IServerAddressesFeature>().Addresses;
                foreach (var url in urls)
                {
                    logger.LogInformation("Listening on " + url);
                }
                host.WaitForShutdown();
            }
            catch (ConfigException ex)
            {
                if (!string.IsNullOrEmpty(ex.Message))
                    Logs.Configuration.LogError(ex.Message);
            }
            catch(Exception e) when( PluginManager.IsExceptionByPlugin(e))
            {
                var pluginDir = new DataDirectories().Configure(conf).PluginDir;
                PluginManager.DisablePlugin(pluginDir, e.Source);
            }
            finally
            {
                processor.Dispose();
                if (host == null)
                    Logs.Configuration.LogError("Configuration error");
                if (host != null)
                    host.Dispose();
                Serilog.Log.CloseAndFlush();
                loggerProvider.Dispose();
            }
        }
    }
}
