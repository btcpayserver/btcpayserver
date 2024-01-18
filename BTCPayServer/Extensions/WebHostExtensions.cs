using System;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Hosting
{
    public static class WebHostExtensions
    {
        public static async Task StartWithTasksAsync(this IWebHost webHost, IConfiguration conf, CancellationToken cancellationToken = default)
        {
            // Load all tasks from DI
            var startupTasks = webHost.Services.GetServices<IStartupTask>();

            // Execute all the tasks
            foreach (var startupTask in startupTasks)
            {
                await startupTask.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            // Start the tasks as normal
            try
            {
                await webHost.StartAsync(cancellationToken).ConfigureAwait(false);
            }
            catch (Exception e) when (PluginManager.IsExceptionByPlugin(e, out var pluginName))
            {
                var pluginDir = new DataDirectories().Configure(conf).PluginDir;
                PluginManager.DisablePlugin(pluginDir, pluginName);
                throw;
            }
        }
    }
}
