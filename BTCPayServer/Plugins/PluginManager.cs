using System;
using System.Collections.Generic;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
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
using Newtonsoft.Json.Linq;

namespace BTCPayServer.Plugins
{
    public static class PluginManager
    {
        public const string BTCPayPluginSuffix = ".btcpay";
        private static readonly List<Assembly> _pluginAssemblies = new ();

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
            void LoadPluginsFromAssemblies(Assembly systemAssembly1, HashSet<string> exclude, HashSet<string> loadedPluginIdentifiers1,
                List<IBTCPayServerPlugin> btcPayServerPlugins)
            {
                // Load the referenced assembly plugins
                // All referenced plugins should have at least one plugin with exact same plugin identifier
                // as the assembly. Except for the system assembly (btcpayserver assembly) which are fake plugins
                foreach (var assembly in AppDomain.CurrentDomain.GetAssemblies())
                {
                    var assemblyName = assembly.GetName().Name;
                    bool isSystemPlugin = assembly == systemAssembly1;
                    if (!isSystemPlugin && exclude.Contains(assemblyName))
                        continue;

                    foreach (var plugin in GetPluginInstancesFromAssembly(assembly))
                    {
                        if (!isSystemPlugin && plugin.Identifier != assemblyName)
                            continue;
                        if (!loadedPluginIdentifiers1.Add(plugin.Identifier))
                            continue;
                        btcPayServerPlugins.Add(plugin);
                        plugin.SystemPlugin = isSystemPlugin;
                    }
                }
            }

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

            var disabledPluginIdentifiers = GetDisabledPluginIdentifiers(pluginsFolder);
            var systemAssembly = typeof(Program).Assembly;
            LoadPluginsFromAssemblies(systemAssembly, disabledPluginIdentifiers, loadedPluginIdentifiers, plugins);

            if (ExecuteCommands(pluginsFolder, plugins.ToDictionary(plugin => plugin.Identifier, plugin => plugin.Version)))
            {
                plugins.Clear();
                loadedPluginIdentifiers.Clear();
                LoadPluginsFromAssemblies(systemAssembly, disabledPluginIdentifiers, loadedPluginIdentifiers, plugins);
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
                if (disabledPluginIdentifiers.Contains(pluginIdentifier))
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
                    QueueCommands(pluginsFolder, ("disable", plugin.Identifier));
                    logger.LogWarning("Please update your prism plugin, this version is incompatible");
                    continue;
                } 
                if (plugin.Identifier == "BTCPayServer.Plugins.Wabisabi" && plugin.Version <= new Version("1.0.66"))
                {
                    
                    QueueCommands(pluginsFolder, ("disable", plugin.Identifier));
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
            return GetTypesNotCrash(assembly).Where(type =>
                typeof(IBTCPayServerPlugin).IsAssignableFrom(type) && type != typeof(PluginService.AvailablePlugin) &&
                !type.IsAbstract).
                Select(type => (IBTCPayServerPlugin)Activator.CreateInstance(type, Array.Empty<object>()));
        }

        private static IEnumerable<Type> GetTypesNotCrash(Assembly assembly)
        {
            try
            {
                // Strange crash with selenium
                if (assembly.FullName.Contains("Selenium", StringComparison.OrdinalIgnoreCase))
                    return Array.Empty<Type>();
                return assembly.GetTypes();
            }
            catch(ReflectionTypeLoadException ex)
            {
                return ex.Types.Where(t => t is not null).ToArray();
            }
        }

        private static IBTCPayServerPlugin GetPluginInstanceFromAssembly(string pluginIdentifier, Assembly assembly)
        {
            return GetPluginInstancesFromAssembly(assembly).FirstOrDefault(plugin => plugin.Identifier == pluginIdentifier);
        }

        private static bool ExecuteCommands(string pluginsFolder, Dictionary<string, Version> installed = null)
        {
            var pendingCommands = GetPendingCommands(pluginsFolder);
            if (!pendingCommands.Any())
            {
                return false;
            }

            var remainingCommands = (from command in pendingCommands where !ExecuteCommand(command, pluginsFolder, false, installed) select $"{command.command}:{command.plugin}").ToList();
            if (remainingCommands.Any())
            {
                File.WriteAllLines(Path.Combine(pluginsFolder, "commands"), remainingCommands);
            }
            else
            {
                File.Delete(Path.Combine(pluginsFolder, "commands"));
            }

            return remainingCommands.Count != pendingCommands.Length;
        }
        private static Dictionary<string, (Version, IBTCPayServerPlugin.PluginDependency[] Dependencies, bool Disabled)> TryGetInstalledInfo(
            string pluginsFolder)
        {
            var disabled = GetDisabledPluginIdentifiers(pluginsFolder);
            var installed = new Dictionary<string, (Version, IBTCPayServerPlugin.PluginDependency[] Dependencies, bool Disabled)>();
            foreach (string pluginDir in Directory.EnumerateDirectories(pluginsFolder))
            {
                var plugin = Path.GetFileName(pluginDir);
                var dirName = Path.Combine(pluginsFolder, plugin);
                var isDisabled = disabled.Contains(plugin);
                var manifestFileName = Path.Combine(dirName, plugin + ".json");
                if (File.Exists(manifestFileName))
                {
                    var pluginManifest =  JObject.Parse(File.ReadAllText(manifestFileName)).ToObject<PluginService.AvailablePlugin>();
                    installed.TryAdd(pluginManifest.Identifier, (pluginManifest.Version, pluginManifest.Dependencies, isDisabled));
                }
                else if (isDisabled)
                {
                    // Disabled plugin might not have a manifest, but we still need to include
                    // it in the list, so that it can be shown on the Manage Plugins page
                    installed.TryAdd(plugin, (null, null, true));
                }
            }
            return installed;
        }

        private static bool DependenciesMet(string pluginsFolder, string plugin, Dictionary<string, Version> installed)
        {
            var dirName = Path.Combine(pluginsFolder, plugin);
            var manifestFileName = dirName + ".json";
            if (!File.Exists(manifestFileName)) return true;
            var pluginManifest =  JObject.Parse(File.ReadAllText(manifestFileName)).ToObject<PluginService.AvailablePlugin>();
            return DependenciesMet(pluginManifest.Dependencies, installed);
        }

        private static bool ExecuteCommand((string command, string extension) command, string pluginsFolder,
            bool ignoreOrder, Dictionary<string, Version> installed)
        {
            var dirName = Path.Combine(pluginsFolder, command.extension);
            switch (command.command)
            {
                case "update":
                    if (!DependenciesMet(pluginsFolder, command.extension, installed))
                        return false;
                    ExecuteCommand(("delete", command.extension), pluginsFolder, true, installed);
                    ExecuteCommand(("install", command.extension), pluginsFolder, true, installed);
                    break;

                case "delete":
                    ExecuteCommand(("enable", command.extension), pluginsFolder, true, installed);
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
                    var fileName = dirName + BTCPayPluginSuffix;
                    var manifestFileName = dirName + ".json";
                    if (!DependenciesMet(pluginsFolder, command.extension, installed))
                        return false;

                    ExecuteCommand(("enable", command.extension), pluginsFolder, true, installed);
                    if (File.Exists(fileName))
                    {
                        ZipFile.ExtractToDirectory(fileName, dirName, true);
                        if (!ignoreOrder)
                        {
                            File.AppendAllLines(Path.Combine(pluginsFolder, "order"), new[] { command.extension });
                        }
                        File.Delete(fileName);
                        if (File.Exists(manifestFileName))
                        {
                            File.Move(manifestFileName, Path.Combine(dirName, Path.GetFileName(manifestFileName)));
                        }
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

            return true;
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

            if (File.Exists(Path.Combine(pluginDir, plugin, BTCPayPluginSuffix)))
            {
                File.Delete(Path.Combine(pluginDir, plugin, BTCPayPluginSuffix));
            } 
            if (File.Exists(Path.Combine(pluginDir, plugin, ".json")))
            {
                File.Delete(Path.Combine(pluginDir, plugin, ".json"));
            }
            File.Delete(Path.Combine(pluginDir, "commands"));
            QueueCommands(pluginDir, cmds);
        }

        public static void DisablePlugin(string pluginDir, string plugin)
        {
            QueueCommands(pluginDir, ("disable", plugin));
        }

        // Loads the list of disabled plugins from the file
        private static HashSet<string> GetDisabledPluginIdentifiers(string pluginsFolder)
        {
            var disabledPath = Path.Combine(pluginsFolder, "disabled");
            return File.Exists(disabledPath) ? File.ReadAllLines(disabledPath).ToHashSet() : [];
        }

        // List of disabled plugins with additional info, like the disabled version and its dependencies
        public static Dictionary<string, Version> GetDisabledPlugins(string pluginsFolder)
        {
            return TryGetInstalledInfo(pluginsFolder).Where(pair => pair.Value.Disabled)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Item1);
        }

        public static bool DependencyMet(IBTCPayServerPlugin.PluginDependency dependency,
            Dictionary<string, Version> installed = null)
        {
            var plugin = dependency.Identifier.ToLowerInvariant();
            var versionReq = dependency.Condition;
            // ensure installed is not null and has lowercased keys for comparison
            installed = installed == null
                ? new Dictionary<string, Version>()
                : installed.ToDictionary(x => x.Key.ToLowerInvariant(), x => x.Value);
            if (!installed.ContainsKey(plugin) && !versionReq.Equals("!"))
            {
                return false;
            }

            var versionConditions = versionReq.Split("||", StringSplitOptions.RemoveEmptyEntries);
            return versionConditions.Any(s =>
            {
                s = s.Trim();
                var v = s.Substring(1);
                if (s[1] == '=')
                {
                    v = s.Substring(2);
                }

                var parsedV = Version.Parse(v);
                switch (s)
                {
                    case { } xx when xx.StartsWith(">="):
                        return installed[plugin] >= parsedV;
                    case { } xx when xx.StartsWith("<="):
                        return installed[plugin] <= parsedV;
                    case { } xx when xx.StartsWith(">"):
                        return installed[plugin] > parsedV;
                    case { } xx when xx.StartsWith("<"):
                        return installed[plugin] >= parsedV;
                    case { } xx when xx.StartsWith("^"):
                        return installed[plugin] >= parsedV && installed[plugin].Major == parsedV.Major;
                    case { } xx when xx.StartsWith("~"):
                        return installed[plugin] >= parsedV && installed[plugin].Major == parsedV.Major &&
                               installed[plugin].Minor == parsedV.Minor;
                    case { } xx when xx.StartsWith("!="):
                        return installed[plugin] != parsedV;
                    case { } xx when xx.StartsWith("=="):
                    default:
                        return installed[plugin] == parsedV;
                }
            });
        }

        public static bool DependenciesMet(IEnumerable<IBTCPayServerPlugin.PluginDependency> dependencies,
            Dictionary<string, Version> installed = null)
        {
            return dependencies.All(dependency => DependencyMet(dependency, installed));
        }
    }
}
