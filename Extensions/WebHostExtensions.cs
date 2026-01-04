using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Hosting;
using Microsoft.Extensions.DependencyInjection;

namespace Microsoft.AspNetCore.Hosting
{
    public static class WebHostExtensions
    {
        public static async Task StartWithTasksAsync(this IWebHost webHost, CancellationToken cancellationToken = default)
        {
            // Load all tasks from DI
            var startupTasks = webHost.Services.GetServices<IStartupTask>();

            // Execute all the tasks
            foreach (var startupTask in startupTasks)
            {
                await startupTask.ExecuteAsync(cancellationToken).ConfigureAwait(false);
            }

            // Start the tasks as normal
            await webHost.StartAsync(cancellationToken).ConfigureAwait(false);
        }
    }
}
