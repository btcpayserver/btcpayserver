#nullable enable
using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text.RegularExpressions;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Abstractions.Extensions;
using BTCPayServer.Configuration;
using BTCPayServer.Plugins.Dotnet;
using BTCPayServer.Services;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Server.Kestrel.Core;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Logging;
using Newtonsoft.Json;
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
        private static PluginLoadingContexts _pluginLoadingContexts = new();
        private static readonly object FailureDiagnosticsLock = new();
        private const string FailureDiagnosticsFileName = "plugin-failures.json";

        public static bool IsExceptionByPlugin(Exception exception, [MaybeNullWhen(false)] out PluginLoadingContext preloadedPlugin)
        {
            if (ExtractPluginFromStackTrace(exception, out preloadedPlugin)) return true;

            var fromAssembly = exception is TypeLoadException
                ? Regex.Match(exception.Message, "from assembly '(.*?),").Groups[1].Value
                : null;
            foreach (var plugin in _pluginLoadingContexts)
            {
                var assembly = plugin.Assembly;
                var assemblyName = assembly.GetName().Name;
                if (assemblyName is null)
                    continue;
                // Comparison is case-sensitive as it is theoretically possible to have a different plugin
                // with the same name but different casing.
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
                // For TypeLoadException, check if it might come from a referenced assembly
                if (!string.IsNullOrEmpty(fromAssembly) && assembly.GetReferencedAssemblies().Select(a => a.Name).Contains(fromAssembly))
                {
                    preloadedPlugin = plugin;
                    return true;
                }
            }
            preloadedPlugin = null;
            return false;
        }

        private static bool ExtractPluginFromStackTrace(Exception exception, [MaybeNullWhen(false)] out PluginLoadingContext preloadedPlugin)
        {
            Dictionary<string, PluginLoadingContext> pluginsByName = new();

            foreach (var preloaded in _pluginLoadingContexts.Where(p => p.Loader is not null && !string.IsNullOrEmpty(p.Assembly.FullName)))
            {
                pluginsByName.TryAdd(preloaded.Assembly.FullName!, preloaded);
                foreach (var assembly in preloaded.Loader!.LoadContext.Assemblies)
                {
                    pluginsByName.TryAdd(assembly.FullName!, preloaded);
                }
            }
            return ExtractPluginFromStackTrace(exception, out preloadedPlugin, pluginsByName);
        }

        private static bool ExtractPluginFromStackTrace(Exception exception, out PluginLoadingContext? preloadedPlugin,
            Dictionary<string, PluginLoadingContext> pluginsByName)
        {
            var trace = new StackTrace(exception, true);
            foreach (var frame in trace.GetFrames().Reverse())
            {
                var m = frame.GetMethod();
                if (m is null)
                    continue;
                if (pluginsByName.TryGetValue(m.Module.Assembly.FullName ?? "", out var plugin))
                {
                    preloadedPlugin = plugin;
                    return true;
                }
            }
            preloadedPlugin = null;
            if (exception is AggregateException aggregateException)
            {
                foreach (var ex in aggregateException.InnerExceptions)
                {
                    if (ExtractPluginFromStackTrace(ex, out preloadedPlugin, pluginsByName))
                        return true;
                }

                return false;
            }
            else if (exception.InnerException is not null)
                return ExtractPluginFromStackTrace(exception.InnerException, out preloadedPlugin, pluginsByName);
            else
                return false;
        }

        public record PreloadedPlugin(IBTCPayServerPlugin Instance, PluginLoader? Loader, Assembly Assembly);

        public sealed class PluginLoadingContext
        {
            public required string PluginDir { get; init; }
            public required string Identifier { get; init; }
            public required string AssemblyPath { get; init; }
            public Version? ManifestVersion { get; init; }
            public PluginLifecyclePhase Phase { get; set; }
            public PreloadedPlugin? PreloadedPlugin { get; private set; }

            public IBTCPayServerPlugin Instance => PreloadedPlugin!.Instance;
            public PluginLoader? Loader => PreloadedPlugin!.Loader;
            public Assembly Assembly => PreloadedPlugin!.Assembly;

            public void SetPreloadedPlugin(PreloadedPlugin plugin)
            {
                if (PreloadedPlugin is not null)
                    throw new InvalidOperationException("The plugin has already been preloaded.");
                PreloadedPlugin = plugin;
            }
        }

        class PluginLoadingContexts : IEnumerable<PluginLoadingContext>
        {
            List<PluginLoadingContext> _plugins = new();
            readonly Dictionary<string, PluginLoadingContext> _contextsByIdentifier = new(StringComparer.OrdinalIgnoreCase);

            public bool Contains(string identifier) => _contextsByIdentifier.ContainsKey(identifier);
            public void Add(PluginLoadingContext plugin)
            {
                if (!_contextsByIdentifier.TryAdd(plugin.Instance.Identifier, plugin))
                    return;
                _plugins.Add(plugin);
            }

            public IEnumerator<PluginLoadingContext> GetEnumerator() => _plugins.GetEnumerator();
            IEnumerator IEnumerable.GetEnumerator() => _plugins.GetEnumerator();

            public void Clear()
            {
                _plugins.Clear();
                _contextsByIdentifier.Clear();
            }

            public void TopologicalSort()
            {
                // We want to run all the system plugins first.
                // Then the rest topologically sorted.
                var ordered = new List<PluginLoadingContext>(_plugins.Count);
                var topological = _plugins.TopologicalSort(
                    p => p.Instance.Dependencies.Select(d => d.Identifier),
                    p => p.Instance.Identifier,
                    p=> p, Comparer<PluginLoadingContext>.Create((a, b) => string.Compare(a.Instance.Identifier, b.Instance.Identifier, StringComparison.Ordinal))).ToList();
                foreach (var p in topological.Where(t => t.Instance.SystemPlugin))
                    ordered.Add(p);
                foreach (var p in topological.Where(t => !t.Instance.SystemPlugin))
                    ordered.Add(p);
                _plugins = ordered;
            }

            public PluginLoadingContext? TryGet(string identifier)
            {
                _contextsByIdentifier.TryGetValue(identifier, out var p);
                return p;
            }
        }

        public static IMvcBuilder AddPlugins(this IMvcBuilder mvcBuilder, IServiceCollection serviceCollection,
            IConfiguration config, ILoggerFactory loggerFactory, ServiceProvider bootstrapServiceProvider)
        {
            var pluginLoadingContexts = new PluginLoadingContexts();
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
                plugin.SystemPlugin = true;
                var context = new PluginLoadingContext
                {
                    PluginDir = pluginsFolder,
                    Identifier = plugin.Identifier,
                    AssemblyPath = systemAssembly.Location,
                    ManifestVersion = plugin.Version,
                    Phase = PluginLifecyclePhase.PluginExecution
                };
                context.SetPreloadedPlugin(new PreloadedPlugin(plugin, null, systemAssembly));
                pluginLoadingContexts.Add(context);
            }

            var pluginsToPreload = new List<PluginLoadingContext>();

#if DEBUG
            // Load from DEBUG_PLUGINS, in an optional appsettings.dev.json
            var debugPlugins = config["DEBUG_PLUGINS"] ?? "";
            foreach (var plugin in debugPlugins.Split(';', StringSplitOptions.RemoveEmptyEntries))
            {
                var contentRoot = config["contentRoot"] as string ?? ".";
                // Formatted either as "<PLUGIN_IDENTIFIER>::<PathToDll>" or "<PathToDll>"
                var idx = plugin.IndexOf("::", StringComparison.Ordinal);
                var filePath = plugin;
                if (idx != -1)
                {
                    filePath = plugin[(idx + 1)..];
                    filePath = Path.GetFullPath(Path.Combine(contentRoot, filePath));
                    var identifier = plugin[0..idx];
                    pluginsToPreload.Add(CreateLoadingContext(pluginsFolder, identifier, filePath));
                }
                else
                {
                    filePath = Path.GetFullPath(Path.Combine(contentRoot, filePath));

                    var identifier = Path.GetFileNameWithoutExtension(plugin);
                    pluginsToPreload.Add(CreateLoadingContext(pluginsFolder, identifier, filePath));
                }
            }
#endif

            // Load from the plugins folder
            foreach (var directory in Directory.GetDirectories(pluginsFolder))
            {
                var pluginIdentifier = Path.GetFileName(directory);
                var pluginFilePath = Path.Join(directory, pluginIdentifier + ".dll");
                if (!File.Exists(pluginFilePath))
                    continue;
                if (disabledPluginIdentifiers.Contains(pluginIdentifier))
                {
                    logger.LogInformation($"Skipping disabled plugin {pluginIdentifier}");
                    continue;
                }
                if (IsIncompatible(pluginIdentifier, TryReadPluginVersion(directory, pluginIdentifier)))
                {
                    logger.LogWarning($"Refusing to load the plugin {pluginIdentifier}, this version cannot be run. It has been disabled and will stay disabled until it is updated to a newer version.");
                    ExecuteCommand(("disable", pluginIdentifier), pluginsFolder);
                    continue;
                }

                pluginsToPreload.Add(CreateLoadingContext(pluginsFolder, pluginIdentifier, pluginFilePath));
            }

            var toDisable = new List<PluginLoadingContext>();

            foreach (var context in pluginsToPreload)
            {
                if (pluginLoadingContexts.Contains(context.Identifier))
                    continue;

                context.Phase = PluginLifecyclePhase.AssemblyLoad;
                try
                {
                    var loader = PluginLoader.CreateFromAssemblyFile(
                        context.AssemblyPath, // create a plugin from for the .dll file
                        c =>
                        {

                            // this ensures that the version of MVC is shared between this app and the plugin
                            c.PreferSharedTypes = true;
                            c.IsUnloadable = false;
                            c.LoadAssembliesInDefaultLoadContext = config.GetOrDefault<bool>("TEST_RUNNER_ENABLED", false);
                        });
                    var pluginAssembly = loader.LoadDefaultAssembly();

                    context.Phase = PluginLifecyclePhase.PluginActivation;
                    var instances = GetPluginInstancesFromAssembly(pluginAssembly, silentlyFails: true).ToArray();
                    var p = instances.FirstOrDefault(plugin => plugin.Identifier == context.Identifier);
                    if (p == null)
                    {
                        context.Phase = PluginLifecyclePhase.PluginDiscovery;
                        var actualIdentifiers = string.Join(", ", instances.Select(plugin => plugin.Identifier));
                        var details = instances.Length == 0
                            ? $"The plugin assembly doesn't contain a plugin with identifier {context.Identifier}."
                            : $"The plugin identifier does not match. Expected {context.Identifier}, but found: {actualIdentifiers}.";
                        logger.LogError(details);
                        RecordPluginFailure(context, details);
                        toDisable.Add(context);
					}
                    else
                    {
                        context.Phase = PluginLifecyclePhase.PluginValidation;
                        if (context.ManifestVersion is not null && p.Version != context.ManifestVersion)
                        {
                            var details = $"The plugin version does not match its manifest. Expected {context.ManifestVersion}, but loaded {p.Version}.";
                            logger.LogError(details);
                            RecordPluginFailure(context, details);
                            toDisable.Add(context);
                            continue;
                        }
                        p.SystemPlugin = false;
                        context.SetPreloadedPlugin(new PreloadedPlugin(p, loader, pluginAssembly));
                        pluginLoadingContexts.Add(context);
                    }
                }
                catch (Exception e)
                {
                    logger.LogError(e, $"Error when loading plugin {context.Identifier}.");
                    RecordPluginFailure(context, e);
                    toDisable.Add(context);
				}
            }

            pluginLoadingContexts.TopologicalSort();
            var loadedPlugins = new List<PluginLoadingContext>();
            foreach (var preloadedPlugin in pluginLoadingContexts)
            {
                var plugin = preloadedPlugin.Instance;
                preloadedPlugin.Phase = PluginLifecyclePhase.PluginExecution;
                try
                {
                    AssertDependencies(plugin, loadedPlugins);
                    if (preloadedPlugin.Loader is { } loader)
                        loader.AddAssemblyLoadContexts(
                            plugin.Dependencies
                            .Select(d => pluginLoadingContexts.TryGet(d.Identifier)?.Loader)
                            .Where(d => d is not null)
                            .ToArray()!);

                    // silentlyFails is false, because we want this to throw if there is any missing assembly.
                    GetPluginInstanceFromAssembly(plugin.Identifier, preloadedPlugin.Assembly, silentlyFails: false);
                    if (preloadedPlugin.Loader is not null)
                        mvcBuilder.AddPluginLoader(preloadedPlugin.Loader);

                    var (logLevel, message) = plugin switch
                    {
                        { Identifier: "BTCPayServer" } => (LogLevel.Information, $"Running {plugin.Identifier} - {BTCPayServerEnvironment.GetInformationalVersion()}"),
                        { SystemPlugin: true } => (LogLevel.Debug, $"Running system plugin {plugin.Identifier} - {plugin.Version}"),
                        _ => (LogLevel.Information, $"Running plugin {plugin.Identifier} - {plugin.Version}")
                    };
                    logger.Log(logLevel, message);
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
                    {
                        RecordPluginFailure(preloadedPlugin, e);
                        toDisable.Add(preloadedPlugin);
                    }
                }
            }
            _pluginLoadingContexts = pluginLoadingContexts;
			if (toDisable.Count > 0)
			{
				foreach (var plugin in toDisable.DistinctBy(plugin => plugin.Identifier, StringComparer.OrdinalIgnoreCase))
					DisablePlugin(plugin);
				var crashedPluginsStr = string.Join(", ", toDisable.Select(plugin => plugin.Identifier));
				throw new ConfigException($"The following plugin(s) crashed at startup, they will be disabled and the server will restart: {crashedPluginsStr}");
			}
			return mvcBuilder;
        }

        class MissingDependenciesException(string message) : Exception(message);

        private static void AssertDependencies(IBTCPayServerPlugin plugin, List<PluginLoadingContext> loaded)
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

        private static PluginLoadingContext CreateLoadingContext(
            string pluginDir,
            string identifier,
            string assemblyPath)
        {
            return new PluginLoadingContext
            {
                PluginDir = pluginDir,
                Identifier = identifier,
                AssemblyPath = assemblyPath,
                ManifestVersion = TryReadPluginVersion(Path.GetDirectoryName(assemblyPath)!, identifier),
                Phase = PluginLifecyclePhase.AssemblyLoad
            };
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
                File.WriteAllLines(Path.Join(pluginsFolder, "commands"), remainingCommands);
            }
            else
            {
                File.Delete(Path.Join(pluginsFolder, "commands"));
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
                var dirName = Path.Join(pluginsFolder, plugin);
                var isDisabled = disabled.Contains(plugin);
                var manifestFileName = Path.Join(dirName, plugin + ".json");
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
            var dirName = Path.Join(pluginsFolder, plugin);
            var manifestFileName = dirName + ".json";
            if (!File.Exists(manifestFileName)) return true;
            var pluginManifest =  JObject.Parse(File.ReadAllText(manifestFileName)).ToObject<PluginService.AvailablePlugin>();
            if (pluginManifest is not null)
                return DependenciesMet(pluginManifest.Dependencies, installed);
            return true;
        }

        private static bool ExecuteCommand((string command, string extension) command, string pluginsFolder)
        {
            AssertSafeName(pluginsFolder, command.extension);
            var dirName = Path.Join(pluginsFolder, command.extension);
            switch (command.command)
            {
                case "delete":
                    ExecuteCommand(("enable", command.extension), pluginsFolder);
                    if (File.Exists(dirName))
                        File.Delete(dirName);
                    if (Directory.Exists(dirName))
                        Directory.Delete(dirName, true);
                    ClearPluginFailure(pluginsFolder, command.extension);
                    break;

                case "install":
                    var fileName = dirName + BTCPayPluginSuffix;
                    var manifestFileName = dirName + ".json";
                    ExecuteCommand(("enable", command.extension), pluginsFolder);
                    if (File.Exists(fileName))
                    {
                        if (File.Exists(dirName))
                            File.Delete(dirName);
                        if (Directory.Exists(dirName))
                            Directory.Delete(dirName, true);

                        ZipFile.ExtractToDirectory(fileName, dirName, true);
                        File.Delete(fileName);
                        if (File.Exists(manifestFileName))
                        {
                            File.Move(manifestFileName, Path.Join(dirName, Path.GetFileName(manifestFileName)), true);
                        }
                    }
                    break;

                case "disable":
                    if (Directory.Exists(dirName))
                    {
                        if (File.Exists(Path.Join(pluginsFolder, "disabled")))
                        {
                            var disabled = File.ReadAllLines(Path.Join(pluginsFolder, "disabled"));
                            if (!disabled.Contains(command.extension))
                            {
                                File.AppendAllLines(Path.Join(pluginsFolder, "disabled"), new[] { command.extension });
                            }
                        }
                        else
                        {
                            File.AppendAllLines(Path.Join(pluginsFolder, "disabled"), new[] { command.extension });
                        }
                    }

                    break;

                case "enable":
                    if (File.Exists(Path.Join(pluginsFolder, "disabled")))
                    {
                        var disabled = File.ReadAllLines(Path.Join(pluginsFolder, "disabled"));
                        if (disabled.Contains(command.extension))
                        {
                            File.WriteAllLines(Path.Join(pluginsFolder, "disabled"), disabled.Where(s => s != command.extension));
                        }
                    }

                    break;
            }

            return true;
        }

        public static (string command, string plugin)[] GetPendingCommands(string pluginsFolder)
        {
            if (!File.Exists(Path.Join(pluginsFolder, "commands")))
                return Array.Empty<(string command, string plugin)>();
            var commands = File.ReadAllLines(Path.Join(pluginsFolder, "commands"));
            return commands.Select(s =>
            {
                var split = s.Split(':');
                if (split.Length != 2)
                    return (string.Empty, string.Empty);
                return (split[0].ToLower(CultureInfo.InvariantCulture), split[1]);
            })
            .Where(s => s.Item1 != "")
            .ToArray();
        }

        public static void QueueCommands(string pluginsFolder, params (string action, string plugin)[] commands)
        {
            File.AppendAllLines(Path.Join(pluginsFolder, "commands"),
                commands.Select((tuple) => $"{tuple.action}:{tuple.plugin}"));
        }

        public static void CancelCommands(string pluginDir, string plugin)
        {
            var cmds = GetPendingCommands(pluginDir).Where(tuple =>
                !tuple.plugin.Equals(plugin, StringComparison.InvariantCultureIgnoreCase)).ToArray();
            AssertSafeName(pluginDir, plugin);
            if (File.Exists(Path.Join(pluginDir, plugin + BTCPayPluginSuffix)))
            {
                File.Delete(Path.Join(pluginDir, plugin + BTCPayPluginSuffix));
            }
            if (File.Exists(Path.Join(pluginDir, plugin + ".json")))
            {
                File.Delete(Path.Join(pluginDir, plugin + ".json"));
            }
            File.Delete(Path.Join(pluginDir, "commands"));
            QueueCommands(pluginDir, cmds);
        }

        private static void AssertSafeName(string pluginDir, string plugin)
        {
            // platform separator: WithTrailingSlash's '/' mismatches GetFullPath on Windows
            var fullPluginDir = Path.GetFullPath(pluginDir);
            if (!fullPluginDir.EndsWith(Path.DirectorySeparatorChar))
                fullPluginDir += Path.DirectorySeparatorChar;
            var fullPath = Path.GetFullPath(Path.Combine(fullPluginDir, plugin));
            if (!fullPath.StartsWith(fullPluginDir, StringComparison.Ordinal))
                throw new InvalidOperationException("File traversal detected");
        }

        public static void DisablePlugins(string pluginDir)
        {
            foreach (var plugin in _pluginLoadingContexts.Where(plugin => !plugin.Instance.SystemPlugin))
                DisablePlugin(pluginDir, plugin.Identifier);
        }

        public static void DisablePlugins(Exception exception)
        {
            foreach (var plugin in _pluginLoadingContexts.Where(plugin => !plugin.Instance.SystemPlugin))
            {
                plugin.Phase = PluginLifecyclePhase.Startup;
                RecordPluginFailure(plugin, exception, PluginFailureAttribution.PossiblyInvolvedCulpritUnknown);
                DisablePlugin(plugin);
            }
        }

        public static void DisablePlugin(PluginLoadingContext plugin)
        {
            if (plugin.PreloadedPlugin?.Instance.SystemPlugin is true) return;
            DisablePlugin(plugin.PluginDir, plugin.Identifier);
        }

        public static void DisablePlugin(PluginLoadingContext plugin, Exception exception,
            PluginLifecyclePhase phase)
        {
            if (plugin.PreloadedPlugin?.Instance.SystemPlugin is true) return;
            plugin.Phase = phase;
            RecordPluginFailure(plugin, exception);
            DisablePlugin(plugin);
        }

        public static void DisablePlugin(string pluginDir, string plugin)
        {
            QueueCommands(pluginDir, ("disable", plugin));
        }

        public static IReadOnlyDictionary<string, PluginFailureDiagnostic> GetPluginFailures(string pluginDir)
        {
            try
            {
                lock (FailureDiagnosticsLock)
                {
                    return ReadPluginFailures(pluginDir);
                }
            }
            catch
            {
                return new Dictionary<string, PluginFailureDiagnostic>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void RecordPluginFailure(
            PluginLoadingContext plugin,
            Exception exception,
            PluginFailureAttribution attribution = PluginFailureAttribution.Confirmed)
        {
            RecordPluginFailure(plugin, exception.ToString(), attribution);
        }

        private static void RecordPluginFailure(
            PluginLoadingContext plugin,
            string details,
            PluginFailureAttribution attribution = PluginFailureAttribution.Confirmed)
        {
            var diagnostic = new PluginFailureDiagnostic
            {
                Timestamp = DateTimeOffset.UtcNow,
                PluginIdentifier = plugin.Identifier,
                PluginVersion = plugin.PreloadedPlugin?.Instance.Version ?? plugin.ManifestVersion,
                BTCPayVersion = BTCPayServerEnvironment.GetInformationalVersion(),
                Phase = plugin.Phase,
                Attribution = attribution,
                Details = details
            };

            try
            {
                lock (FailureDiagnosticsLock)
                {
                    var failures = ReadPluginFailures(plugin.PluginDir);
                    failures[plugin.Identifier] = diagnostic;
                    WritePluginFailures(plugin.PluginDir, failures);
                }
            }
            catch
            {
                // Diagnostics must never prevent crash protection from disabling a plugin.
            }
        }

        public static void ClearFailureDiagnosticsForStartedPlugins(
            string pluginDir,
            IEnumerable<IBTCPayServerPlugin> startedPlugins)
        {
            var identifiers = startedPlugins
                .Where(plugin => !plugin.SystemPlugin)
                .Select(plugin =>
                {
                    var context = _pluginLoadingContexts.TryGet(plugin.Identifier);
                    if (context is not null)
                        context.Phase = PluginLifecyclePhase.Running;
                    return context?.Identifier ?? plugin.Identifier;
                })
                .ToArray();
            ClearPluginFailures(pluginDir, identifiers);
        }

        public static void ClearPluginFailure(string pluginDir, string identifier)
        {
            ClearPluginFailures(pluginDir, [identifier]);
        }

        private static void ClearPluginFailures(string pluginDir, IEnumerable<string> identifiers)
        {
            try
            {
                lock (FailureDiagnosticsLock)
                {
                    var failures = ReadPluginFailures(pluginDir);
                    var changed = false;
                    foreach (var identifier in identifiers)
                        changed |= failures.Remove(identifier);
                    if (changed)
                        WritePluginFailures(pluginDir, failures);
                }
            }
            catch
            {
                // Diagnostics cleanup must never prevent the server from starting.
            }
        }

        private static Dictionary<string, PluginFailureDiagnostic> ReadPluginFailures(string pluginDir)
        {
            var path = Path.Join(pluginDir, FailureDiagnosticsFileName);
            if (!File.Exists(path))
                return new Dictionary<string, PluginFailureDiagnostic>(StringComparer.OrdinalIgnoreCase);
            try
            {
                var persisted = JsonConvert.DeserializeObject<Dictionary<string, PluginFailureDiagnostic>>(
                    File.ReadAllText(path));
                return persisted is null
                    ? new Dictionary<string, PluginFailureDiagnostic>(StringComparer.OrdinalIgnoreCase)
                    : new Dictionary<string, PluginFailureDiagnostic>(persisted, StringComparer.OrdinalIgnoreCase);
            }
            catch (JsonException)
            {
                return new Dictionary<string, PluginFailureDiagnostic>(StringComparer.OrdinalIgnoreCase);
            }
            catch (IOException)
            {
                return new Dictionary<string, PluginFailureDiagnostic>(StringComparer.OrdinalIgnoreCase);
            }
        }

        private static void WritePluginFailures(string pluginDir,
            Dictionary<string, PluginFailureDiagnostic> failures)
        {
            Directory.CreateDirectory(pluginDir);
            var path = Path.Join(pluginDir, FailureDiagnosticsFileName);
            if (failures.Count == 0)
            {
                File.Delete(path);
                return;
            }

            File.WriteAllText(path, JsonConvert.SerializeObject(failures, Formatting.Indented));
        }


        internal static Version? TryReadPluginVersion(string pluginDirectory, string pluginIdentifier)
        {
            return TryReadPluginManifest(pluginDirectory, pluginIdentifier)?.Version;
        }

        private static PluginService.AvailablePlugin? TryReadPluginManifest(
            string pluginDirectory,
            string pluginIdentifier)
        {
            var manifestFileName = Path.Join(pluginDirectory, pluginIdentifier + ".json");
            if (!File.Exists(manifestFileName))
                return null;
            try
            {
                return JObject.Parse(File.ReadAllText(manifestFileName))
                    .ToObject<PluginService.AvailablePlugin>();
            }
            catch
            {
                return null;
            }
        }

        private static readonly Dictionary<string, Version> IncompatiblePlugins =
            new(StringComparer.OrdinalIgnoreCase)
            {
                { "BTCPayServer.Plugins.ShopifyPlugin", new Version(1, 1, 5) },
                { "BTCPayServer.Plugins.Ecwid", new Version(1, 1, 0) },
                { "SamRockProtocol", new Version(1, 1, 0) },
                { "BTCPayServer.Plugins.Stripe", new Version(1, 0, 12) },
                { "BTCPayServer.Plugins.BigCommercePlugin", new Version(1, 0, 7) },
                { "BTCPayServer.RockstarDev.Plugins.MarkPaidCheckout", new Version(0, 1, 2) },
                { "BTCPayServer.Plugins.ArkPayServer", new Version(2, 4, 2) },
                { "BTCPayServer.Plugins.Cashu", new Version(1, 0, 4) }
            };

        internal static bool IsIncompatible(string? pluginIdentifier, Version? version)
        {
            if (pluginIdentifier is null || !IncompatiblePlugins.TryGetValue(pluginIdentifier, out var lastAffected))
                return false;
            if (version is null)
                return true;
            return TruncateToBuild(version) <= TruncateToBuild(lastAffected);
        }

        private static Version TruncateToBuild(Version version)
            => new(version.Major, version.Minor, Math.Max(version.Build, 0));

        // Loads the list of disabled plugins from the file
        private static HashSet<string> GetDisabledPluginIdentifiers(string pluginsFolder)
        {
            var disabledPath = Path.Join(pluginsFolder, "disabled");
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
