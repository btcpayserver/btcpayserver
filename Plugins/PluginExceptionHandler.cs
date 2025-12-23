using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Threading;
using System.Threading.Tasks;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Features;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Hosting.Internal;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;

namespace BTCPayServer.Plugins
{
    public class PluginExceptionHandler : IExceptionHandler
    {
        readonly string _pluginDir;
        readonly IHostApplicationLifetime _applicationLifetime;
        private readonly Logs _logs;

        public PluginExceptionHandler(IOptions<DataDirectories> options, IHostApplicationLifetime applicationLifetime, Logs logs)
        {
            _applicationLifetime = applicationLifetime;
            _logs = logs;
            _pluginDir = options.Value.PluginDir;
        }
        public ValueTask<bool> TryHandleAsync(HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
        {
            if (!GetDisablePluginIfCrash(httpContext) ||
                !PluginManager.IsExceptionByPlugin(exception, out var pluginName))
                return ValueTask.FromResult(false);
            _logs.Configuration.LogError(exception, $"Unhandled exception caused by plugin '{pluginName}', disabling it and restarting...");

            if (Debugger.IsAttached)
            {
                _logs.Configuration.LogWarning("Debugger attached detected, so we didn't disable the plugin and do not restart the server");
                return ValueTask.FromResult(false);
            }
            PluginManager.DisablePlugin(_pluginDir, pluginName);
            _ = Task.Delay(3000).ContinueWith((t) => _applicationLifetime.StopApplication());
            // Returning true here means we will see Error 500 error message.
            // Returning false means that the user will see a stacktrace.
            return ValueTask.FromResult(false);
        }

        public static bool GetDisablePluginIfCrash(HttpContext httpContext)
        {
            return httpContext.Items.TryGetValue("DisablePluginIfCrash", out object renderingDashboard) ||
                            renderingDashboard is not true;
        }
        public static void SetDisablePluginIfCrash(HttpContext httpContext)
        {
            httpContext.Items.TryAdd("DisablePluginIfCrash", true);
        }
    }
}
