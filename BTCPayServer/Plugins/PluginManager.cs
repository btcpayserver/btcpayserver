using System;
using System.Collections.Generic;
using System.Collections.Specialized;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Configuration;
using BTCPayServer.Logging;
using McMaster.NETCore.Plugins;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using NBXplorer;

namespace BTCPayServer.Plugins
{
    public static class PluginManager
    {
        public const string BTCPayPluginSuffix = ".btcpay";
        private static readonly List<Assembly> _pluginAssemblies = new List<Assembly>();

        public static bool IsExceptionByPlugin(Exception exception, [MaybeNullWhen(false)] out string pluginName)
        {
            var fromAssembly = exception is TypeLoadException
                ? Regex.Match(exception.Message, "from assembly '(.*?),").Groups[1].Value
                : null;

            foreach (var assembly in _pluginAssemblies)
            {
                var assemblyName = assembly.GetName().Name;
                if (assemblyName is null)
                    continue;
                // Comparison is case sensitive as it is theoretically possible to have a different plugin
                // with same name but different casing.
                if (exception.Source is not null &&
                    assemblyName.Equals(exception.Source, StringComparison.Ordinal))
                {
                    pluginName = assemblyName;
                    return true;
                }
                if (exception.Message.Contains(assemblyName, StringComparison.Ordinal))
                {
                    pluginName = assemblyName;
                    return true;
                }
                // For TypeLoadException, check if it might come from areferenced assembly
                if (!string.IsNullOrEmpty(fromAssembly) && assembly.GetReferencedAssemblies().Select(a => a.Name).Contains(fromAssembly))
                {
                    pluginName = assemblyName;
                    return true;
                }
            }
            pluginName = null;
            return false;
        }
        public static IMvcBuilder AddPlugins(this IMvcBuilder mvcBuilder, IServiceCollection serviceCollection,
            IConfiguration config, ILoggerFactory loggerFactory, ServiceProvider bootstrapServiceProvider)
        {
            var logger = loggerFactory.CreateLogger(typeof(PluginManager));
            var pluginsFolder = new DataDirectories().Configure(config).PluginDir;
            var plugins = new List<IBTCPayServerPlugin>();
            var loadedPluginIdentifiers = new HashSet<string>();

            serviceCollection.Configure<KestrelServerOptions>(options =>
            {
                options.Limits.MaxRequestBodySize = int.MaxValue; // if don't set default value is: 30 MB
            });
            logger.LogInformation($"Loading plugins from {pluginsFolder}");
            Directory.CreateDirectory(pluginsFolder);
            ExecuteCommands(pluginsFolder);

            var disabledPlugins = GetDisabledPlugins(pluginsFolder);
            var systemAssembly = typeof(Program).Assembly;
            // Load the referenced assembly plugins
            // All referenced plugins should have at least one plugin with exact same plugin identifier
            // as the assembly. Except for the system assembly (btcpayserver assembly) which are fake plugins
            foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
            {
                var assemblyName = assembly.GetName().Name;
                bool isSystemPlugin = assembly == systemAssembly;
                if (!isSystemPlugin && disabledPlugins.Contains(assemblyName))
                    continue;

                foreach (var plugin in GetPluginInstancesFromAssembly(assembly))
                {
                    if (!isSystemPlugin && plugin.Identifier != assemblyName)
                        continue;
                    if (!loadedPluginIdentifiers.Add(plugin.Identifier))
                        continue;
                    plugins.Add(plugin);
                    plugin.SystemPlugin = isSystemPlugin;
                }
            }

            var pluginsToLoad = new List<(string PluginIdentifier, string PluginFilePath)>();

#if DEBUG
            // Load from DEBUG_PLUGINS, in an optional appsettings.dev.json
            var debugPlugins = config["DEBUG_PLUGINS"] ?? "";
            foreach (var plugin in debugPlugins.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                // Formatted either as "<PLUGIN_IDENTIFIER>::<PathToDll>" or "<PathToDll>"
                var idx = plugin.IndexOf("::");
                if (idx != -1)
                    pluginsToLoad.Add((plugin[0..idx], plugin[(idx + 1)..]));
                else
                    pluginsToLoad.Add((Path.GetFileNameWithoutExtension(plugin), plugin));
            }
#endif

            // Load from the plugins folder
            foreach (var directory in Directory.GetDirectories(pluginsFolder))
            {
                var pluginIdentifier = Path.GetFileName(directory);
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
                // This used to be a standalone plugin but due to popular demand has been made as part of core. If we detect an install, we remove the redundant plugin.
                if (toLoad.PluginIdentifier == "BTCPayServer.Plugins.NFC")
                {
                    QueueCommands(pluginsFolder, ("delete", toLoad.PluginIdentifier));
                    continue;
                }
                if (!loadedPluginIdentifiers.Add(toLoad.PluginIdentifier))
                    continue;
                try
                {

                    var plugin = PluginLoader.CreateFromAssemblyFile(
                        toLoad.PluginFilePath, // create a plugin from for the .dll file
                        config =>
                        {

                            // this ensures that the version of MVC is shared between this app and the plugin
                            config.PreferSharedTypes = true;
                            config.IsUnloadable = false;
                        });
                    var pluginAssembly = plugin.LoadDefaultAssembly();

                    var p = GetPluginInstanceFromAssembly(toLoad.PluginIdentifier, pluginAssembly);
                    if (p == null)
                    {
                        logger.LogError($"The plugin assembly doesn't contain the plugin {toLoad.PluginIdentifier}");
                    }
                    else
                    {
                        mvcBuilder.AddPluginLoader(plugin);
                        _pluginAssemblies.Add(pluginAssembly);
                        p.SystemPlugin = false;
                        plugins.Add(p);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e,
                        $"Error when loading plugin {toLoad.PluginIdentifier}");
                }
            }

            foreach (var plugin in plugins)
            {
                if (plugin.Identifier == "BTCPayServer.Plugins.Prism" && plugin.Version <= new Version("1.1.18"))
                {
                    logger.LogWarning("Please update your prism plugin, this version is incompatible");
                    continue;
                }
                try
                {
                    logger.LogInformation(
                        $"Adding and executing plugin {plugin.Identifier} - {plugin.Version}");
                    var pluginServiceCollection = new PluginServiceCollection(serviceCollection, bootstrapServiceProvider);
                    plugin.Execute(pluginServiceCollection);
                    serviceCollection.AddSingleton(plugin);
                }
                catch (Exception e)
                {
                    logger.LogError(
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
            pluginsToLoad.Sort((a, b) => ordersByPlugin[a.PluginIdentifier] - ordersByPlugin[b.PluginIdentifier]);
        }

        public static void UsePlugins(this IApplicationBuilder applicationBuilder)
        {
            HashSet<Assembly> assemblies = new HashSet<Assembly>();
            foreach (var extension in applicationBuilder.ApplicationServices
                .GetServices<IBTCPayServerPlugin>())
            {
                extension.Execute(applicationBuilder,
                    applicationBuilder.ApplicationServices);
                assemblies.Add(extension.GetType().Assembly);
            }

            var webHostEnvironment = applicationBuilder.ApplicationServices.GetService<IWebHostEnvironment>();
            List<IFileProvider> providers = new List<IFileProvider>() { webHostEnvironment.WebRootFileProvider };
            providers.AddRange(assemblies.Select(a => new EmbeddedFileProvider(a)));
            webHostEnvironment.WebRootFileProvider = new CompositeFileProvider(providers);
        }

        private static IEnumerable<IBTCPayServerPlugin> GetPluginInstancesFromAssembly(Assembly assembly)
        {
            return assembly.GetTypes().Where(type =>
                typeof(IBTCPayServerPlugin).IsAssignableFrom(type) && type != typeof(PluginService.AvailablePlugin) &&
                !type.IsAbstract).
                Select(type => (IBTCPayServerPlugin)Activator.CreateInstance(type, Array.Empty<object>()));
        }
        private static IBTCPayServerPlugin GetPluginInstanceFromAssembly(string pluginIdentifier, Assembly assembly)
        {
            return GetPluginInstancesFromAssembly(assembly)
                .Where(plugin => plugin.Identifier == pluginIdentifier)
                .FirstOrDefault();
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
