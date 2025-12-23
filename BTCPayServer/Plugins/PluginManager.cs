#nullable enable
using System;
using System.Collections;
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
using BTCPayServer.Plugins.Dotnet;
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
        /// <summary>
        /// In case of tests, this is shared the plugins that are already their assembly loaded.
        /// This avoid loading the same plugin twice.
        /// </summary>
        private static PreloadedPlugins _preloadedPlugins = new();

        public static bool IsExceptionByPlugin(Exception exception, [MaybeNullWhen(false)] out PreloadedPlugin preloadedPlugin)
        {
            var fromAssembly = exception is TypeLoadException
                ? Regex.Match(exception.Message, "from assembly '(.*?),").Groups[1].Value
                : null;

            foreach (var plugin in _preloadedPlugins)
            {
                var assembly = plugin.Assembly;
                var assemblyName = assembly.GetName().Name;
                if (assemblyName is null)
                    continue;
                // Comparison is case sensitive as it is theoretically possible to have a different plugin
                // with same name but different casing.
                if (exception.Source is not null &&
                    assemblyName.Equals(exception.Source, StringComparison.Ordinal))
                {
                    preloadedPlugin = plugin;
                    return true;
                }
                if (exception.Message.Contains(assemblyName, StringComparison.Ordinal))
                {
                    preloadedPlugin = plugin;
                    return true;
                }
                // For TypeLoadException, check if it might come from areferenced assembly
                if (!string.IsNullOrEmpty(fromAssembly) && assembly.GetReferencedAssemblies().Select(a => a.Name).Contains(fromAssembly))
                {
                    preloadedPlugin = plugin;
                    return true;
                }
            }
            preloadedPlugin = null;
            return false;
        }

        public record PreloadedPlugin(IBTCPayServerPlugin Instance, PluginLoader? Loader, Assembly Assembly);

        class PreloadedPlugins : IEnumerable<PreloadedPlugin>
        {
            List<PreloadedPlugin> _plugins = new();
            readonly Dictionary<string, PreloadedPlugin> _preloadedPluginsByIdentifier = new(StringComparer.OrdinalIgnoreCase);

            public bool Contains(string identifier) => _preloadedPluginsByIdentifier.ContainsKey(identifier);
            public void Add(PreloadedPlugin  plugin)
            {
                if (!_preloadedPluginsByIdentifier.TryAdd(plugin.Instance.Identifier, plugin))
                    return;
                _plugins.Add(plugin);
            }

            public IEnumerator<PreloadedPlugin> GetEnumerator() => _plugins.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => _plugins.GetEnumerator();

            public void Clear()
            {
                _plugins.Clear();
                _preloadedPluginsByIdentifier.Clear();
            }

            public void TopologicalSort()
            {
                // We want to run all the system plugins first.
                // Then the rest topologically sorted.
                var ordered = new List<PreloadedPlugin>(_plugins.Count);
                var topological = _plugins.TopologicalSort(
                    p => p.Instance.Dependencies.Select(d => d.Identifier),
                    p => p.Instance.Identifier,
                    p=> p, Comparer<PreloadedPlugin>.Create((a, b) => string.Compare(a.Instance.Identifier, b.Instance.Identifier, StringComparison.Ordinal))).ToList();
                foreach (var p in topological.Where(t => t.Instance.SystemPlugin))
                    ordered.Add(p);
                foreach (var p in topological.Where(t => !t.Instance.SystemPlugin))
                    ordered.Add(p);
                _plugins = ordered;
            }

            public PreloadedPlugin? TryGet(string identifier)
            {
                _preloadedPluginsByIdentifier.TryGetValue(identifier, out var p);
                return p;
            }
        }

        public static IMvcBuilder AddPlugins(this IMvcBuilder mvcBuilder, IServiceCollection serviceCollection,
            IConfiguration config, ILoggerFactory loggerFactory, ServiceProvider bootstrapServiceProvider)
        {
            var preloadedPlugins = new PreloadedPlugins();
            var logger = loggerFactory.CreateLogger(typeof(PluginManager));
            var pluginsFolder = new DataDirectories().Configure(config).PluginDir;

            serviceCollection.Configure<KestrelServerOptions>(options =>
            {
                options.Limits.MaxRequestBodySize = int.MaxValue; // if don't set default value is: 30 MB
            });
            logger.LogInformation($"Loading plugins from {pluginsFolder}");
            Directory.CreateDirectory(pluginsFolder);
            ExecuteCommands(pluginsFolder);

            var disabledPluginIdentifiers = GetDisabledPluginIdentifiers(pluginsFolder);
            var systemAssembly = typeof(Program).Assembly;
            foreach (var plugin in GetPluginInstancesFromAssembly(systemAssembly, true))
            {
                preloadedPlugins.Add(new PreloadedPlugin(plugin, null, systemAssembly));
                plugin.SystemPlugin = true;
            }

            var pluginsToPreload = new List<(string PluginIdentifier, string PluginFilePath)>();

#if DEBUG
            // Load from DEBUG_PLUGINS, in an optional appsettings.dev.json
            var debugPlugins = config["DEBUG_PLUGINS"] ?? "";
            foreach (var plugin in debugPlugins.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                // Formatted either as "<PLUGIN_IDENTIFIER>::<PathToDll>" or "<PathToDll>"
                var idx = plugin.IndexOf("::", StringComparison.Ordinal);
                var filePath = plugin;
                if (idx != -1)
                {
                    filePath = plugin[(idx + 1)..];
                    filePath = Path.GetFullPath(filePath);
                    pluginsToPreload.Add((plugin[0..idx], filePath));
                }
                else
                {
                    filePath = Path.GetFullPath(filePath);

                    pluginsToPreload.Add((Path.GetFileNameWithoutExtension(plugin), filePath));
                }
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
                pluginsToPreload.Add((pluginIdentifier, pluginFilePath));
            }

            var toDisable = new List<string>();

            foreach (var toLoad in pluginsToPreload)
            {
                if (preloadedPlugins.Contains(toLoad.PluginIdentifier))
                    continue;

                try
                {
                    var loader = PluginLoader.CreateFromAssemblyFile(
                        toLoad.PluginFilePath, // create a plugin from for the .dll file
                        c =>
                        {

                            // this ensures that the version of MVC is shared between this app and the plugin
                            c.PreferSharedTypes = true;
                            c.IsUnloadable = false;
                            c.LoadAssembliesInDefaultLoadContext = config.GetOrDefault<bool>("TEST_RUNNER_ENABLED", false);
                        });
                    var pluginAssembly = loader.LoadDefaultAssembly();

                    var p = GetPluginInstanceFromAssembly(toLoad.PluginIdentifier, pluginAssembly, silentlyFails: true);
                    if (p == null)
                    {
                        logger.LogError($"The plugin assembly doesn't contain the plugin {toLoad.PluginIdentifier}");
						toDisable.Add(toLoad.PluginIdentifier);
					}
                    else
                    {
                        p.SystemPlugin = false;
                        preloadedPlugins.Add(new(p, loader, pluginAssembly));
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Error when loading plugin {toLoad.PluginIdentifier}.");
					toDisable.Add(toLoad.PluginIdentifier);
				}
            }

            preloadedPlugins.TopologicalSort();
            var loadedPlugins = new List<PreloadedPlugin>();
            foreach (var preloadedPlugin in preloadedPlugins)
            {
                var plugin = preloadedPlugin.Instance;
                try
                {
                    AssertDependencies(plugin, loadedPlugins);
                    if (preloadedPlugin.Loader is { } loader)
                        loader.AddAssemblyLoadContexts(
                            plugin.Dependencies
                            .Select(d => preloadedPlugins.TryGet(d.Identifier)?.Loader)
                            .Where(d => d is not null)
                            .ToArray()!);

                    // silentlyFails is false, because we want this to throw if there is any missing assembly.
                    GetPluginInstanceFromAssembly(plugin.Identifier, preloadedPlugin.Assembly, silentlyFails: false);
                    if (preloadedPlugin.Loader is not null)
                        mvcBuilder.AddPluginLoader(preloadedPlugin.Loader);

                    logger.Log(plugin.SystemPlugin ? LogLevel.Debug : LogLevel.Information,
                        $"Adding and executing plugin {plugin.Identifier} - {plugin.Version}");
                    var pluginServiceCollection = new PluginServiceCollection(serviceCollection, bootstrapServiceProvider);
                    plugin.Execute(pluginServiceCollection);
                    serviceCollection.AddSingleton(plugin);
                    loadedPlugins.Add(preloadedPlugin);
                }
                catch (MissingDependenciesException e)
                {
                    // The difference is that we don't print the stacktrace and we do not disable it
                    logger.LogError($"Error when executing plugin {plugin.Identifier} - {plugin.Version}: {e.Message}");
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Error when executing plugin {plugin.Identifier} - {plugin.Version}.");
                    if (!plugin.SystemPlugin)
                        toDisable.Add(plugin.Identifier);
                }
            }
            _preloadedPlugins = preloadedPlugins;
			if (toDisable.Count > 0)
			{
				foreach (var plugin in toDisable)
					DisablePlugin(pluginsFolder, plugin);
				var crashedPluginsStr = string.Join(", ", toDisable);
				throw new ConfigException($"The following plugin(s) crashed at startup, they will be disabled and the server will restart: {crashedPluginsStr}");
			}
			return mvcBuilder;
        }

        class MissingDependenciesException(string message) : Exception(message);

        private static void AssertDependencies(IBTCPayServerPlugin plugin, List<PreloadedPlugin> loaded)
        {
            var missing = new List<IBTCPayServerPlugin.PluginDependency>();
            var installed = loaded.ToDictionary(l => l.Instance.Identifier, l => l.Instance.Version);
            foreach (var d in plugin.Dependencies)
            {
                if (!DependencyMet(d, installed))
                {
                    missing.Add(d);
                }
            }
            if (missing.Any())
            {
                throw new MissingDependenciesException(
                    $"Plugin {plugin.Identifier} is missing dependencies: {string.Join(", ", missing.Select(d => d.ToString()))}");
            }
        }

        public static void UsePlugins(this IApplicationBuilder applicationBuilder)
        {
            var assemblies = new HashSet<Assembly>();
            foreach (var extension in applicationBuilder.ApplicationServices
                .GetServices<IBTCPayServerPlugin>())
            {
                extension.Execute(applicationBuilder,
                    applicationBuilder.ApplicationServices);
                assemblies.Add(extension.GetType().Assembly);
            }

            var webHostEnvironment = applicationBuilder.ApplicationServices.GetRequiredService<IWebHostEnvironment>();
            var providers = new List<IFileProvider>() { webHostEnvironment.WebRootFileProvider };
            providers.AddRange(assemblies.Select(a => new EmbeddedFileProvider(a)));
            webHostEnvironment.WebRootFileProvider = new CompositeFileProvider(providers);
        }

        private static IEnumerable<IBTCPayServerPlugin> GetPluginInstancesFromAssembly(Assembly assembly, bool silentlyFails)
        {
            return GetTypes(assembly, silentlyFails).Where(type =>
                typeof(IBTCPayServerPlugin).IsAssignableFrom(type) && type != typeof(PluginService.AvailablePlugin) &&
                !type.IsAbstract).
                Select(type => Activator.CreateInstance(type, Array.Empty<object>()) as IBTCPayServerPlugin)
                .Where(t => t is not null)!;
        }

        private static IEnumerable<Type> GetTypes(Assembly assembly, bool silentlyFails)
        {
            try
            {
                return assembly.GetTypes();
            }
            catch (ReflectionTypeLoadException ex) when (silentlyFails)
            {
                return ex.Types.Where(t => t is not null)!;
            }
        }

        private static IBTCPayServerPlugin? GetPluginInstanceFromAssembly(string pluginIdentifier, Assembly assembly, bool silentlyFails)
        {
            return GetPluginInstancesFromAssembly(assembly, silentlyFails).FirstOrDefault(plugin => plugin.Identifier == pluginIdentifier);
        }

        private static bool ExecuteCommands(string pluginsFolder)
        {
            var pendingCommands = GetPendingCommands(pluginsFolder);
            if (!pendingCommands.Any())
            {
                return false;
            }

            var remainingCommands = (from command in pendingCommands where !ExecuteCommand(command, pluginsFolder) select $"{command.command}:{command.plugin}").ToList();
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
        private static Dictionary<string, (Version?, IBTCPayServerPlugin.PluginDependency[]? Dependencies, bool Disabled)> TryGetInstalledInfo(
            string pluginsFolder)
        {
            var disabled = GetDisabledPluginIdentifiers(pluginsFolder);
            var installed = new Dictionary<string, (Version?, IBTCPayServerPlugin.PluginDependency[]? Dependencies, bool Disabled)>();
            foreach (var pluginDir in Directory.EnumerateDirectories(pluginsFolder))
            {
                var plugin = Path.GetFileName(pluginDir);
                var dirName = Path.Combine(pluginsFolder, plugin);
                var isDisabled = disabled.Contains(plugin);
                var manifestFileName = Path.Combine(dirName, plugin + ".json");
                if (File.Exists(manifestFileName))
                {
                    var pluginManifest =  JObject.Parse(File.ReadAllText(manifestFileName)).ToObject<PluginService.AvailablePlugin>();
                    if (pluginManifest is not null)
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
            if (pluginManifest is not null)
                return DependenciesMet(pluginManifest.Dependencies, installed);
            return true;
        }

        private static bool ExecuteCommand((string command, string extension) command, string pluginsFolder)
        {
            var dirName = Path.Combine(pluginsFolder, command.extension);
            switch (command.command)
            {
                case "delete":
                    ExecuteCommand(("enable", command.extension), pluginsFolder);
                    if (File.Exists(dirName))
                    {
                        File.Delete(dirName);
                    }
                    if (Directory.Exists(dirName))
                    {
                        Directory.Delete(dirName, true);
                    }
                    break;

                case "install":
                    var fileName = dirName + BTCPayPluginSuffix;
                    var manifestFileName = dirName + ".json";
                    ExecuteCommand(("enable", command.extension), pluginsFolder);

                    if (File.Exists(fileName))
                    {
                        if (File.Exists(dirName) || Directory.Exists(dirName))
                            ExecuteCommand(("delete", dirName), pluginsFolder);
                        ZipFile.ExtractToDirectory(fileName, dirName, true);
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

        public static void DisablePlugins(string pluginDir)
        {
            foreach (var plugin in _preloadedPlugins)
                DisablePlugin(pluginDir, plugin);
        }

        public static void DisablePlugin(string pluginDir, PreloadedPlugin plugin)
        {
            if (plugin.Instance.SystemPlugin) return;
            var name = plugin.Assembly.GetName()?.Name;
            if (name is null) return;
            DisablePlugin(pluginDir, name);
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
        public static Dictionary<string, Version?> GetDisabledPlugins(string pluginsFolder)
        {
            return TryGetInstalledInfo(pluginsFolder).Where(pair => pair.Value.Disabled)
                .ToDictionary(pair => pair.Key, pair => pair.Value.Item1);
        }

        public static bool DependencyMet(IBTCPayServerPlugin.PluginDependency dependency,
            Dictionary<string, Version> installed)
        {
            var condition = dependency.ParseCondition();
            if (!installed.TryGetValue(dependency.Identifier, out var v))
                return condition is VersionCondition.Not;
            return condition.IsFulfilled(v);
        }

        public static bool DependenciesMet(IEnumerable<IBTCPayServerPlugin.PluginDependency> dependencies,
            Dictionary<string, Version> installed) => dependencies.All(dependency => DependencyMet(dependency, installed));
    }
}
