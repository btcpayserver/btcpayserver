using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using BTCPayServer.Abstractions.Contracts;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins
{
    public class PluginHookService : IPluginHookService
    {
        private readonly IEnumerable<IPluginHookAction> _actions;
        private readonly IEnumerable<IPluginHookFilter> _filters;
        private readonly ILogger<PluginHookService> _logger;

        public PluginHookService(IEnumerable<IPluginHookAction> actions, IEnumerable<IPluginHookFilter> filters,
            ILogger<PluginHookService> logger)
        {
            _actions = actions;
            _filters = filters;
            _logger = logger;
        }

        // Trigger simple action hook for registered plugins
        public async Task ApplyAction(string hook, object args)
        {
            ActionInvoked?.Invoke(this, (hook, args));
            var filters = _actions
                .Where(filter => filter.Hook.Equals(hook, StringComparison.InvariantCultureIgnoreCase)).ToList();
            foreach (IPluginHookAction pluginHookFilter in filters)
            {
                try
                {
                    await pluginHookFilter.Execute(args);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Action on hook {hook} failed");
                }
            }
        }

        // Trigger hook on which registered plugins can optionally return modified args or new object back
        public async Task<object> ApplyFilter(string hook, object args)
        {
            FilterInvoked?.Invoke(this, (hook, args));
            var filters = _filters
                .Where(filter => filter.Hook.Equals(hook, StringComparison.InvariantCultureIgnoreCase)).ToList();
            foreach (IPluginHookFilter pluginHookFilter in filters)
            {
                try
                {
                    args = await pluginHookFilter.Execute(args);
                }
                catch (Exception e)
                {
                    _logger.LogError(e, $"Filter on hook {hook} failed");
                }
            }

            return args;
        }

        public event EventHandler<(string hook, object args)> ActionInvoked;
        public event EventHandler<(string hook, object args)> FilterInvoked;
    }
}
