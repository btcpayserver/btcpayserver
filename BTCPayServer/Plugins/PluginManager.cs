using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Runtime.InteropServices.ComTypes;
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
        private static readonly List<PluginLoader> _plugins = new List<PluginLoader>();
        private static ILogger _logger;

        public static bool IsExceptionByPlugin(Exception exception)
        {
           return  _pluginAssemblies.Any(assembly => assembly.FullName.Contains(exception.Source));
        }
        public static IMvcBuilder AddPlugins(this IMvcBuilder mvcBuilder, IServiceCollection serviceCollection,
            IConfiguration config, ILoggerFactory loggerFactory)
        {
            _logger = loggerFactory.CreateLogger(typeof(PluginManager));
            var pluginsFolder = new DataDirectories().Configure(config).PluginDir;
            var plugins = new List<IBTCPayServerPlugin>();

            serviceCollection.Configure<KestrelServerOptions>(options =>
            {
                options.Limits.MaxRequestBodySize = int.MaxValue; // if don't set default value is: 30 MB
            });
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

            var disabledPlugins = GetDisabledPlugins(pluginsFolder);
           
            

            foreach (var dir in orderedDirs)
            {
                var pluginName = Path.GetFileName(dir);
                if (disabledPlugins.Contains(pluginName))
                {
                    continue;
                }
                
                var plugin = PluginLoader.CreateFromAssemblyFile(
                    Path.Combine(dir, pluginName + ".dll"), // create a plugin from for the .dll file
                    config =>
                    {
                        
                        // this ensures that the version of MVC is shared between this app and the plugin
                        config.PreferSharedTypes = true;
                        config.IsUnloadable = true;
                    });

                mvcBuilder.AddPluginLoader(plugin);
                var pluginAssembly = plugin.LoadDefaultAssembly();
                _pluginAssemblies.Add(pluginAssembly);
                _plugins.Add(plugin);
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
                    ExecuteCommand(("enable", command.extension), pluginsFolder, true);
                    ExecuteCommand(("delete", command.extension), pluginsFolder, true);
                    ExecuteCommand(("install", command.extension), pluginsFolder, true);
                    break;
                case "delete":
                    
                    ExecuteCommand(("enable", command.extension), pluginsFolder, true);
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
                            File.AppendAllLines(Path.Combine(pluginsFolder, "order"), new[] {command.extension});
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
                                File.AppendAllLines(Path.Combine(pluginsFolder, "disabled"), new []{ command.extension});
                            }
                        }
                        else
                        {
                            File.AppendAllLines(Path.Combine(pluginsFolder, "disabled"), new []{ command.extension});
                        }
                    }

                    break;
                
                case "enable":
                    if (Directory.Exists(dirName))
                    {
                        if (File.Exists(Path.Combine(pluginsFolder, "disabled")))
                        {
                            var disabled = File.ReadAllLines(Path.Combine(pluginsFolder, "disabled"));
                            if (!disabled.Contains(command.extension))
                            {
                                File.WriteAllLines(Path.Combine(pluginsFolder, "disabled"), disabled.Where(s=> s!= command.extension));
                            }
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

        public static void DisablePlugin(string pluginDir, string plugin)
        {
            
            QueueCommands(pluginDir, ("disable",plugin));
        }

        public static void Unload()
        {
            _plugins.ForEach(loader => loader.Dispose());
        }

        public static string[] GetDisabledPlugins(string pluginsFolder)
        {
            var disabledFilePath = Path.Combine(pluginsFolder, "disabled");
            if (File.Exists(disabledFilePath))
            {
                return File.ReadLines(disabledFilePath).ToArray();
            }

            return Array.Empty<string>();
        }
    }
}
