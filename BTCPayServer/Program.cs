using System.IO;
using System.Net;
using System.Runtime.CompilerServices;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Hosting;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Hosting.Server.Features;
using Microsoft.Extensions.Logging;

[assembly: InternalsVisibleTo("BTCPayServer.Tests")]
namespace BTCPayServer
{
    class Program
    {
        static async Task Main(string[] args)
        {
            ServicePointManager.DefaultConnectionLimit = 100;
            IWebHost host = null;
            var processor = new ConsoleLoggerProcessor();
            CustomConsoleLogProvider loggerProvider = new CustomConsoleLogProvider(processor);
            using var loggerFactory = new LoggerFactory();
            loggerFactory.AddProvider(loggerProvider);
            var logger = loggerFactory.CreateLogger("Configuration");
            try
            {
                // This is the only way that LoadArgs can print to console. Because LoadArgs is called by the HostBuilder before Logs.Configure is called
                var conf = new DefaultConfiguration() { Logger = logger }.CreateConfiguration(args);
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
                await host.StartWithTasksAsync();
                var urls = host.ServerFeatures.Get<IServerAddressesFeature>().Addresses;
                foreach (var url in urls)
                {
                    logger.LogInformation("Listening on " + url);
                }
                await host.WaitForShutdownAsync();
            }
            catch (ConfigException ex)
            {
                if (!string.IsNullOrEmpty(ex.Message))
                    Logs.Configuration.LogError(ex.Message);
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
