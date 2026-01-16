#nullable enable
// Copyright (c) Nate McMaster.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BTCPayServer.Plugins.Dotnet.Internal;
using BTCPayServer.Plugins.Dotnet.Loader;

namespace BTCPayServer.Plugins.Dotnet
{
    /// <summary>
    /// This loader attempts to load binaries for execution (both managed assemblies and native libraries)
    /// in the same way that .NET Core would if they were originally part of the .NET Core application.
    /// <para>
    /// This loader reads configuration files produced by .NET Core (.deps.json and runtimeconfig.json)
    /// as well as a custom file (*.config files). These files describe a list of .dlls and a set of dependencies.
    /// The loader searches the plugin path, as well as any additionally specified paths, for binaries
    /// which satisfy the plugin's requirements.
    /// </para>
    /// </summary>
    public class PluginLoader : IDisposable
    {
        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="isUnloadable">Enable unloading the plugin from memory.</param>
        /// <param name="sharedTypes">
        /// <para>
        /// A list of types which should be shared between the host and the plugin.
        /// </para>
        /// <para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </para>
        /// </param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile, bool isUnloadable, Type[] sharedTypes)
            => CreateFromAssemblyFile(assemblyFile, isUnloadable, sharedTypes, _ => { });

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="isUnloadable">Enable unloading the plugin from memory.</param>
        /// <param name="sharedTypes">
        /// <para>
        /// A list of types which should be shared between the host and the plugin.
        /// </para>
        /// <para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </para>
        /// </param>
        /// <param name="configure">A function which can be used to configure advanced options for the plugin loader.</param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile, bool isUnloadable, Type[] sharedTypes, Action<PluginConfig> configure)
        {
            return CreateFromAssemblyFile(assemblyFile,
                    sharedTypes,
                    config =>
                    {
                        config.IsUnloadable = isUnloadable;
                        configure(config);
                    });
        }

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="sharedTypes">
        /// <para>
        /// A list of types which should be shared between the host and the plugin.
        /// </para>
        /// <para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </para>
        /// </param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile, Type[] sharedTypes)
            => CreateFromAssemblyFile(assemblyFile, sharedTypes, _ => { });

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="sharedTypes">
        /// <para>
        /// A list of types which should be shared between the host and the plugin.
        /// </para>
        /// <para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </para>
        /// </param>
        /// <param name="configure">A function which can be used to configure advanced options for the plugin loader.</param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile, Type[] sharedTypes, Action<PluginConfig> configure)
        {
            return CreateFromAssemblyFile(assemblyFile,
                    config =>
                    {
                        if (sharedTypes != null)
                        {
                            var uniqueAssemblies = new HashSet<Assembly>();
                            foreach (var type in sharedTypes)
                            {
                                uniqueAssemblies.Add(type.Assembly);
                            }

                            foreach (var assembly in uniqueAssemblies)
                            {
                                config.SharedAssemblies.Add(assembly.GetName());
                            }
                        }
                        configure(config);
                    });
        }

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile)
            => CreateFromAssemblyFile(assemblyFile, _ => { });

        /// <summary>
        /// Create a plugin loader for an assembly file.
        /// </summary>
        /// <param name="assemblyFile">The file path to the main assembly for the plugin.</param>
        /// <param name="configure">A function which can be used to configure advanced options for the plugin loader.</param>
        /// <returns>A loader.</returns>
        public static PluginLoader CreateFromAssemblyFile(string assemblyFile, Action<PluginConfig> configure)
        {
            if (configure == null)
            {
                throw new ArgumentNullException(nameof(configure));
            }

            var config = new PluginConfig(assemblyFile);
            configure(config);
            return new PluginLoader(config);
        }

        private readonly PluginConfig _config;
        private ManagedLoadContext _context;
        private readonly AssemblyLoadContextBuilder _contextBuilder;
        private volatile bool _disposed;

        private FileSystemWatcher? _fileWatcher;
        private Debouncer? _debouncer;

        /// <summary>
        /// Initialize an instance of <see cref="PluginLoader" />
        /// </summary>
        /// <param name="config">The configuration for the plugin.</param>
        public PluginLoader(PluginConfig config)
        {
            _config = config ?? throw new ArgumentNullException(nameof(config));
            _contextBuilder = CreateLoadContextBuilder(config);
            _context = (ManagedLoadContext)_contextBuilder.Build();
            if (config.EnableHotReload)
            {
                StartFileWatcher();
            }
        }

        /// <summary>
        /// True when this plugin is capable of being unloaded.
        /// </summary>
        public bool IsUnloadable => _context.IsCollectible;


        /// <summary>
        /// This event is raised when the plugin has been reloaded.
        /// If <see cref="PluginConfig.EnableHotReload" /> was set to <c>true</c>,
        /// the plugin will be reloaded when files on disk are changed.
        /// </summary>
        public event PluginReloadedEventHandler? Reloaded;

        /// <summary>
        /// The unloads and reloads the plugin assemblies.
        /// This method throws if <see cref="IsUnloadable" /> is <c>false</c>.
        /// </summary>
        public void Reload()
        {
            EnsureNotDisposed();

            if (!IsUnloadable)
            {
                throw new InvalidOperationException("Reload cannot be used because IsUnloadable is false");
            }

            _context.Unload();
            _context = (ManagedLoadContext)_contextBuilder.Build();
            GC.Collect();
            GC.WaitForPendingFinalizers();
            Reloaded?.Invoke(this, new PluginReloadedEventArgs(this));
        }

        private void StartFileWatcher()
        {
            /*
            This is a very simple implementation.
            Some improvements that could be made in the future:

                * Watch all directories which contain assemblies that could be loaded
                * Support a polling file watcher.
                * Handle delete/recreate better.

            If you're interested in making improvements, feel free to send a pull request.
            */

            _debouncer = new Debouncer(_config.ReloadDelay);

            var watchedDir = Path.GetDirectoryName(_config.MainAssemblyPath);
            if (watchedDir == null)
            {
                throw new InvalidOperationException("Could not determine which directory to watch. "
                + "Please set MainAssemblyPath to an absolute path so its parent directory can be discovered.");
            }

            _fileWatcher = new FileSystemWatcher
            {
                Path = watchedDir
            };
            _fileWatcher.Changed += OnFileChanged;
            _fileWatcher.Filter = "*.dll";
            _fileWatcher.NotifyFilter = NotifyFilters.LastWrite;
            _fileWatcher.EnableRaisingEvents = true;
        }

        private void OnFileChanged(object source, FileSystemEventArgs e)
        {
            if (!_disposed)
            {
                _debouncer?.Execute(Reload);
            }
        }

        internal AssemblyLoadContext LoadContext => _context;

        /// <summary>
        /// Load the main assembly for the plugin.
        /// </summary>
        public Assembly LoadDefaultAssembly()
        {
            EnsureNotDisposed();
            return _context.LoadAssemblyFromFilePath(_config.MainAssemblyPath);
        }

        /// <summary>
        /// Load an assembly by name.
        /// </summary>
        /// <param name="assemblyName">The assembly name.</param>
        /// <returns>The assembly.</returns>
        public Assembly LoadAssembly(AssemblyName assemblyName)
        {
            EnsureNotDisposed();
            return _context.LoadFromAssemblyName(assemblyName);
        }

        /// <summary>
        /// Load an assembly from path.
        /// </summary>
        /// <param name="assemblyPath">The assembly path.</param>
        /// <returns>The assembly.</returns>
        public Assembly LoadAssemblyFromPath(string assemblyPath)
            => _context.LoadAssemblyFromFilePath(assemblyPath);

        /// <summary>
        /// Load an assembly by name.
        /// </summary>
        /// <param name="assemblyName">The assembly name.</param>
        /// <returns>The assembly.</returns>
        public Assembly LoadAssembly(string assemblyName)
        {
            EnsureNotDisposed();
            return LoadAssembly(new AssemblyName(assemblyName));
        }

        /// <summary>
        /// Sets the scope used by some System.Reflection APIs which might trigger assembly loading.
        /// <para>
        /// See https://github.com/dotnet/coreclr/blob/v3.0.0/Documentation/design-docs/AssemblyLoadContext.ContextualReflection.md for more details.
        /// </para>
        /// </summary>
        /// <returns></returns>
        public AssemblyLoadContext.ContextualReflectionScope EnterContextualReflection()
            => _context.EnterContextualReflection();

        /// <summary>
        /// Disposes the plugin loader. This only does something if <see cref="IsUnloadable" /> is true.
        /// When true, this will unload assemblies which which were loaded during the lifetime
        /// of the plugin.
        /// </summary>
        public void Dispose()
        {
            if (_disposed)
            {
                return;
            }

            _disposed = true;

            if (_fileWatcher != null)
            {
                _fileWatcher.EnableRaisingEvents = false;
                _fileWatcher.Changed -= OnFileChanged;
                _fileWatcher.Dispose();
            }

            _debouncer?.Dispose();

            if (_context.IsCollectible)
            {
                _context.Unload();
            }
        }

        private void EnsureNotDisposed()
        {
            if (_disposed)
            {
                throw new ObjectDisposedException(nameof(PluginLoader));
            }
        }

        private static AssemblyLoadContextBuilder CreateLoadContextBuilder(PluginConfig config)
        {
            var builder = new AssemblyLoadContextBuilder();

            builder.SetMainAssemblyPath(config.MainAssemblyPath);
            builder.SetDefaultContext(config.DefaultContext);
            if (config.LoadAssembliesInDefaultLoadContext)
            {
                builder.LoadAssembliesInDefaultLoadContext();
            }

            foreach (var ext in config.PrivateAssemblies)
            {
                builder.PreferLoadContextAssembly(ext);
            }

            if (config.PreferSharedTypes)
            {
                builder.PreferDefaultLoadContext(true);
            }

            if (config.IsUnloadable || config.EnableHotReload)
            {
                builder.EnableUnloading();
            }

            if (config.LoadInMemory)
            {
                builder.PreloadAssembliesIntoMemory();
                builder.ShadowCopyNativeLibraries();
            }

            builder.IsLazyLoaded(config.IsLazyLoaded);
            foreach (var assemblyName in config.SharedAssemblies)
            {
                builder.PreferDefaultLoadContextAssembly(assemblyName);
            }

            var baseDir = Path.GetDirectoryName(config.MainAssemblyPath);
            var assemblyFileName = Path.GetFileNameWithoutExtension(config.MainAssemblyPath);

            if (baseDir == null)
            {
                throw new InvalidOperationException("Could not determine which directory to watch. "
                + "Please set MainAssemblyPath to an absolute path so its parent directory can be discovered.");
            }

            var pluginRuntimeConfigFile = Path.Combine(baseDir, assemblyFileName + ".runtimeconfig.json");

            builder.TryAddAdditionalProbingPathFromRuntimeConfig(pluginRuntimeConfigFile, includeDevConfig: true, out _);

            // Always include runtimeconfig.json from the host app.
            // in some cases, like `dotnet test`, the entry assembly does not actually match with the
            // runtime config file which is why we search for all files matching this extensions.
            foreach (var runtimeconfig in Directory.GetFiles(AppContext.BaseDirectory, "*.runtimeconfig.json"))
            {
                builder.TryAddAdditionalProbingPathFromRuntimeConfig(runtimeconfig, includeDevConfig: true, out _);
            }

            return builder;
        }

        public void AddAssemblyLoadContexts(IEnumerable<PluginLoader> pluginLoaders) => _context.AddAssemblyLoadContexts(pluginLoaders.Select(p => p.LoadContext));
    }
}
