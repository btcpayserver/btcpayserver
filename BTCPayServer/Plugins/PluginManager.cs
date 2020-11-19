using System;
using System.Collections.Generic;
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

        public static IMvcBuilder AddPlugins(this IMvcBuilder mvcBuilder, IServiceCollection serviceCollection,
            IConfiguration config, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(typeof(PluginManager));
            var pluginsFolder = config.GetPluginDir(DefaultConfiguration.GetNetworkType(config));
            var plugins = new List<IBTCPayServerPlugin>();


            _logger.LogInformation($"Loading plugins from {pluginsFolder}");
            Directory.CreateDirectory(pluginsFolder);
            ExecuteCommands(pluginsFolder);
            List<(PluginLoader, Assembly, IFileProvider)> loadedPlugins =
                new List<(PluginLoader, Assembly, IFileProvider)>();
            var systemExtensions = GetDefaultLoadedPluginAssemblies();
            plugins.AddRange(systemExtensions.SelectMany(assembly =>
                GetAllPluginTypesFromAssembly(assembly).Select(GetPluginInstanceFromType)));
            foreach (IBTCPayServerPlugin btcPayServerExtension in plugins)
            {
                btcPayServerExtension.SystemPlugin = true;
            }

            var orderFilePath = Path.Combine(pluginsFolder, "order");
            var availableDirs = Directory.GetDirectories(pluginsFolder);
            var orderedDirs = new List<string>();
            if (File.Exists(orderFilePath))
            {
                var order = File.ReadLines(orderFilePath);
                foreach (var s in order)
                {
                    if (availableDirs.Contains(s))
                    {
                        orderedDirs.Add(s);
                    }
                }

                orderedDirs.AddRange(availableDirs.Where(s => !orderedDirs.Contains(s)));
            }
            else
            {
                orderedDirs = availableDirs.ToList();
            }

            foreach (var dir in orderedDirs)
            {
                var pluginName = Path.GetFileName(dir);

                var plugin = PluginLoader.CreateFromAssemblyFile(
                    Path.Combine(dir, pluginName + ".dll"), // create a plugin from for the .dll file
                    config =>
                        // this ensures that the version of MVC is shared between this app and the plugin
                        config.PreferSharedTypes = true);

                mvcBuilder.AddPluginLoader(plugin);
                var pluginAssembly = plugin.LoadDefaultAssembly();
                _pluginAssemblies.Add(pluginAssembly);
                var fileProvider = CreateEmbeddedFileProviderForAssembly(pluginAssembly);
                loadedPlugins.Add((plugin, pluginAssembly, fileProvider));
                plugins.AddRange(GetAllPluginTypesFromAssembly(pluginAssembly)
                    .Select(GetPluginInstanceFromType));
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

        public static void UsePlugins(this IApplicationBuilder applicationBuilder)
        {
            foreach (var extension in applicationBuilder.ApplicationServices
                .GetServices<IBTCPayServerPlugin>())
            {
                extension.Execute(applicationBuilder,
                    applicationBuilder.ApplicationServices);
            }

            var webHostEnvironment = applicationBuilder.ApplicationServices.GetService<IWebHostEnvironment>();
            List<IFileProvider> providers = new List<IFileProvider>() {webHostEnvironment.WebRootFileProvider};
            providers.AddRange(
                _pluginAssemblies
                    .Select(CreateEmbeddedFileProviderForAssembly));
            webHostEnvironment.WebRootFileProvider = new CompositeFileProvider(providers);
        }

        private static Assembly[] GetDefaultLoadedPluginAssemblies()
        {
            return AppDomain.CurrentDomain.GetAssemblies()
                .ToArray();
        }

        private static Type[] GetAllPluginTypesFromAssembly(Assembly assembly)
        {
            return assembly.GetTypes().Where(type =>
                typeof(IBTCPayServerPlugin).IsAssignableFrom(type) && type != typeof(PluginService.AvailablePlugin) &&
                !type.IsAbstract).ToArray();
        }

        private static IBTCPayServerPlugin GetPluginInstanceFromType(Type type)
        {
            return (IBTCPayServerPlugin)Activator.CreateInstance(type, Array.Empty<object>());
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
                    ExecuteCommand(("delete", command.extension), pluginsFolder, true);
                    ExecuteCommand(("install", command.extension), pluginsFolder, true);
                    break;
                case "delete":
                    if (Directory.Exists(dirName))
                    {
                        Directory.Delete(dirName, true);
                        if (!ignoreOrder && File.Exists(Path.Combine(pluginsFolder, "order")))
                        {
                            var orders = File.ReadAllLines(Path.Combine(pluginsFolder, "order"));
                            File.AppendAllLines(Path.Combine(pluginsFolder, "order"),
                                orders.Where(s => s != command.extension));
                        }
                    }

                    break;
                case "install":
                    var fileName = dirName + BTCPayPluginSuffix;
                    if (File.Exists(fileName))
                    {
                        ZipFile.ExtractToDirectory(fileName, dirName, true);
                        if (!ignoreOrder)
                        {
                            File.AppendAllLines(Path.Combine(pluginsFolder, "order"), new[] {command.extension});
                        }

                        File.Delete(fileName);
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

        public static void QueueCommands(string pluginsFolder, params ( string action, string plugin)[] commands)
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
    }
}
