using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using McMaster.NETCore.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;

namespace BTCPayServer.Plugins
{
    public static class PluginManager
    {
        public const string BTCPayPluginSuffix = ".btcpay";
        private static readonly List<Assembly> _pluginAssemblies = new List<Assembly>();
        private static ILogger _logger;

        private static List<(PluginLoader, Assembly, IFileProvider)> loadedPlugins;
        public static bool IsExceptionByPlugin(Exception exception)
        {
            return _pluginAssemblies.Any(assembly => assembly?.FullName?.Contains(exception.Source!, StringComparison.OrdinalIgnoreCase) is true);
        }
        public static IMvcBuilder AddPlugins(this IMvcBuilder mvcBuilder, IServiceCollection serviceCollection,
            IConfiguration config, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(typeof(PluginManager));
            var pluginsFolder = new DataDirectories().Configure(config).PluginDir;
            var plugins = new List<IBTCPayServerPlugin>();
            var loadedPluginIdentifiers = new HashSet<string>();

            serviceCollection.Configure<KestrelServerOptions>(options =>
            {
                options.Limits.MaxRequestBodySize = int.MaxValue; // if don't set default value is: 30 MB
            });
            _logger.LogInformation($"Loading plugins from {pluginsFolder}");
            Directory.CreateDirectory(pluginsFolder);
            ExecuteCommands(pluginsFolder);


            loadedPlugins = new List<(PluginLoader, Assembly, IFileProvider)>();

            // System plugins directly hard coded in btcpay
            var systemAssembly = typeof(Program).Assembly;
            foreach (IBTCPayServerPlugin plugin in new IBTCPayServerPlugin[]
            {
                new Plugins.BTCPayServerPlugin(),
                new Plugins.Shopify.ShopifyPlugin(),
                new Plugins.PayButton.CrowdfundPlugin(),
                new Plugins.PayButton.PayButtonPlugin(),
                new Plugins.PayButton.PointOfSalePlugin(),
            })
            {
                loadedPluginIdentifiers.Add(plugin.Identifier);
                plugins.Add(plugin);
                plugin.SystemPlugin = true;
                loadedPlugins.Add((null, systemAssembly, CreateEmbeddedFileProviderForAssembly(systemAssembly)));
            }

            var disabledPlugins = GetDisabledPlugins(pluginsFolder);

            var pluginsToLoad = new List<(string PluginIdentifier, string PluginFilePath)>();

#if DEBUG
            // Load from DEBUG_PLUGINS, in an optional appsettings.dev.json
            var debugPlugins = config["DEBUG_PLUGINS"];
            debugPlugins.Split(';', StringSplitOptions.RemoveEmptyEntries);
            foreach (var plugin in debugPlugins.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                // Formatted either as "<PLUGIN_IDENTIFIER>::<PathToDll>" or "<PathToDll>"
                var idx = plugin.IndexOf("::");
                if (idx != -1)
                    pluginsToLoad.Add((plugin[0..idx], plugin[(idx+1)..]));
                else
                    pluginsToLoad.Add((Path.GetFileNameWithoutExtension(plugin), plugin));
            }
#endif

            // Load from the plugins folder
            foreach (var directory in Directory.GetDirectories(pluginsFolder))
            {
                var pluginIdentifier = Path.GetDirectoryName(directory);
                var pluginFilePath = Path.Combine(directory, pluginIdentifier + ".dll");
                if (!File.Exists(pluginFilePath))
                    continue;
                if (disabledPlugins.Contains(pluginIdentifier))
                    continue;
                pluginsToLoad.Add((pluginIdentifier, pluginFilePath));
            }

            ReorderPlugins(pluginsFolder, pluginsToLoad);

            foreach (var toLoad in pluginsToLoad)
            {
                if (loadedPluginIdentifiers.Contains(toLoad.PluginIdentifier))
                    continue;
                try
                {

                    var plugin = PluginLoader.CreateFromAssemblyFile(
                        toLoad.PluginFilePath, // create a plugin from for the .dll file
                        config =>
                        {

                            // this ensures that the version of MVC is shared between this app and the plugin
                            config.PreferSharedTypes = true;
                            config.IsUnloadable = true;
                        });
                    var pluginAssembly = plugin.LoadDefaultAssembly();

                    var p = GetPluginInstanceFromAssembly(pluginAssembly);
                    if (p == null)
                    {
                        _logger.LogError($"The plugin assembly doesn't contain any plugin: {toLoad.PluginIdentifier}");
                    }
                    else
                    {
                        if (toLoad.PluginIdentifier == p.Identifier)
                        {
                            mvcBuilder.AddPluginLoader(plugin);
                            _pluginAssemblies.Add(pluginAssembly);
                            var fileProvider = CreateEmbeddedFileProviderForAssembly(pluginAssembly);
                            loadedPlugins.Add((plugin, pluginAssembly, fileProvider));
                            p.SystemPlugin = false;
                            plugins.Add(p);
                        }
                        else
                        {
                            _logger.LogError($"The plugin Identifier doesn't match the expected one: Expected: {toLoad.PluginIdentifier}, Actual: {p.Identifier}");
                        }
                    }
                }
                catch (Exception e)
                {
                    _logger.LogError(e,
                        $"Error when loading plugin {toLoad.PluginIdentifier}");
                }
            }

            foreach (var plugin in plugins)
            {
                try
                {
                    _logger.LogInformation(
                        $"Adding and executing plugin {plugin.Identifier} - {plugin.Version}");
                    plugin.Execute(serviceCollection);
                    serviceCollection.AddSingleton(plugin);
                }
                catch (Exception e)
                {
                    _logger.LogError(
                        $"Error when loading plugin {plugin.Identifier} - {plugin.Version}{Environment.NewLine}{e.Message}");
                }
            }

            return mvcBuilder;
        }

        private static void ReorderPlugins(string pluginsFolder, List<(string PluginIdentifier, string PluginFilePath)> pluginsToLoad)
        {
            Dictionary<string, int> ordersByPlugin = new Dictionary<string, int>();
            var orderFilePath = Path.Combine(pluginsFolder, "order");
            int order = 0;
            if (File.Exists(orderFilePath))
            {
                foreach (var o in File.ReadLines(orderFilePath))
                {
                    if (ordersByPlugin.TryAdd(o, order))
                        order++;
                }
            }
            foreach (var p in pluginsToLoad)
            {
                if (ordersByPlugin.TryAdd(p.PluginIdentifier, order))
                    order++;
            }
            pluginsToLoad.Sort((a,b) => ordersByPlugin[a.PluginIdentifier] - ordersByPlugin[b.PluginIdentifier]);
        }

        public static void UsePlugins(this IApplicationBuilder applicationBuilder)
        {
            foreach (var extension in applicationBuilder.ApplicationServices
                .GetServices<IBTCPayServerPlugin>())
            {
                extension.Execute(applicationBuilder,
                    applicationBuilder.ApplicationServices);
            }

            var webHostEnvironment = applicationBuilder.ApplicationServices.GetService<IWebHostEnvironment>();
            List<IFileProvider> providers = new List<IFileProvider>() { webHostEnvironment.WebRootFileProvider };
            providers.AddRange(loadedPlugins.Select(tuple => tuple.Item3));
            webHostEnvironment.WebRootFileProvider = new CompositeFileProvider(providers);
        }

        private static IBTCPayServerPlugin GetPluginInstanceFromAssembly(Assembly assembly)
        {
            var plugins = assembly.GetTypes().Where(type =>
                typeof(IBTCPayServerPlugin).IsAssignableFrom(type) && type != typeof(PluginService.AvailablePlugin) &&
                !type.IsAbstract).ToArray();
            var type = plugins.FirstOrDefault();
            if (type != null)
                return (IBTCPayServerPlugin)Activator.CreateInstance(type, Array.Empty<object>());
            return null;
        }

        private static IFileProvider CreateEmbeddedFileProviderForAssembly(Assembly assembly)
        {
            return new EmbeddedFileProvider(assembly);
        }

        private static void ExecuteCommands(string pluginsFolder)
        {
            var pendingCommands = GetPendingCommands(pluginsFolder);
            foreach (var command in pendingCommands)
            {
                ExecuteCommand(command, pluginsFolder);
            }

            File.Delete(Path.Combine(pluginsFolder, "commands"));
        }

        private static void ExecuteCommand((string command, string extension) command, string pluginsFolder,
            bool ignoreOrder = false)
        {
            var dirName = Path.Combine(pluginsFolder, command.extension);
            switch (command.command)
            {
                case "update":
                    ExecuteCommand(("enable", command.extension), pluginsFolder, true);
                    ExecuteCommand(("delete", command.extension), pluginsFolder, true);
                    ExecuteCommand(("install", command.extension), pluginsFolder, true);
                    break;
                case "delete":

                    ExecuteCommand(("enable", command.extension), pluginsFolder, true);
                    if (File.Exists(dirName))
                    {
                        File.Delete(dirName);
                    }
                    if (Directory.Exists(dirName))
                    {
                        Directory.Delete(dirName, true);
                        if (!ignoreOrder && File.Exists(Path.Combine(pluginsFolder, "order")))
                        {
                            var orders = File.ReadAllLines(Path.Combine(pluginsFolder, "order"));
                            File.WriteAllLines(Path.Combine(pluginsFolder, "order"),
                                orders.Where(s => s != command.extension));
                        }
                    }

                    break;
                case "install":
                    ExecuteCommand(("enable", command.extension), pluginsFolder, true);
                    var fileName = dirName + BTCPayPluginSuffix;
                    if (File.Exists(fileName))
                    {
                        ZipFile.ExtractToDirectory(fileName, dirName, true);
                        if (!ignoreOrder)
                        {
                            File.AppendAllLines(Path.Combine(pluginsFolder, "order"), new[] { command.extension });
                        }

                        File.Delete(fileName);
                    }

                    break;

                case "disable":
                    if (Directory.Exists(dirName))
                    {
                        if (File.Exists(Path.Combine(pluginsFolder, "disabled")))
                        {
                            var disabled = File.ReadAllLines(Path.Combine(pluginsFolder, "disabled"));
                            if (!disabled.Contains(command.extension))
                            {
                                File.AppendAllLines(Path.Combine(pluginsFolder, "disabled"), new[] { command.extension });
                            }
                        }
                        else
                        {
                            File.AppendAllLines(Path.Combine(pluginsFolder, "disabled"), new[] { command.extension });
                        }
                    }

                    break;

                case "enable":
                    if (File.Exists(Path.Combine(pluginsFolder, "disabled")))
                    {
                        var disabled = File.ReadAllLines(Path.Combine(pluginsFolder, "disabled"));
                        if (disabled.Contains(command.extension))
                        {
                            File.WriteAllLines(Path.Combine(pluginsFolder, "disabled"), disabled.Where(s => s != command.extension));
                        }
                    }

                    break;
            }
        }

        public static (string command, string plugin)[] GetPendingCommands(string pluginsFolder)
        {
            if (!File.Exists(Path.Combine(pluginsFolder, "commands")))
                return Array.Empty<(string command, string plugin)>();
            var commands = File.ReadAllLines(Path.Combine(pluginsFolder, "commands"));
            return commands.Select(s =>
            {
                var split = s.Split(':');
                return (split[0].ToLower(CultureInfo.InvariantCulture), split[1]);
            }).ToArray();
        }

        public static void QueueCommands(string pluginsFolder, params (string action, string plugin)[] commands)
        {
            File.AppendAllLines(Path.Combine(pluginsFolder, "commands"),
                commands.Select((tuple) => $"{tuple.action}:{tuple.plugin}"));
        }

        public static void CancelCommands(string pluginDir, string plugin)
        {
            var cmds = GetPendingCommands(pluginDir).Where(tuple =>
                !tuple.plugin.Equals(plugin, StringComparison.InvariantCultureIgnoreCase)).ToArray();

            File.Delete(Path.Combine(pluginDir, "commands"));
            QueueCommands(pluginDir, cmds);
        }

        public static void DisablePlugin(string pluginDir, string plugin)
        {

            QueueCommands(pluginDir, ("disable", plugin));
        }

        public static HashSet<string> GetDisabledPlugins(string pluginsFolder)
        {
            var disabledFilePath = Path.Combine(pluginsFolder, "disabled");
            if (File.Exists(disabledFilePath))
            {
                return File.ReadLines(disabledFilePath).ToHashSet();
            }

            return new HashSet<string>();
        }
    }
}
