// Copyright (c) Nate McMaster.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;

namespace BTCPayServer.Plugins.Dotnet
{
    /// <summary>
    /// Represents the configuration for a .NET Core plugin.
    /// </summary>
    public class PluginConfig
    {
        /// <summary>
        /// Initializes a new instance of <see cref="PluginConfig" />
        /// </summary>
        /// <param name="mainAssemblyPath">The full file path to the main assembly for the plugin.</param>
        public PluginConfig(string mainAssemblyPath)
        {
            if (string.IsNullOrEmpty(mainAssemblyPath))
            {
                throw new ArgumentException("Value must be null or not empty", nameof(mainAssemblyPath));
            }

            if (!Path.IsPathRooted(mainAssemblyPath))
            {
                throw new ArgumentException("Value must be an absolute file path", nameof(mainAssemblyPath));
            }

            MainAssemblyPath = mainAssemblyPath;
        }

        /// <summary>
        /// The file path to the main assembly.
        /// </summary>
        public string MainAssemblyPath { get; }

        /// <summary>
        /// A list of assemblies which should be treated as private.
        /// </summary>
        public ICollection<AssemblyName> PrivateAssemblies { get; protected set; } = new List<AssemblyName>();

        /// <summary>
        /// A list of assemblies which should be unified between the host and the plugin.
        /// </summary>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        public ICollection<AssemblyName> SharedAssemblies { get; protected set; } = new List<AssemblyName>();

        /// <summary>
        /// Attempt to unify all types from a plugin with the host.
        /// <para>
        /// This does not guarantee types will unify.
        /// </para>
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md">
        /// https://github.com/natemcmaster/DotNetCorePlugins/blob/main/docs/what-are-shared-types.md
        /// </seealso>
        /// </summary>
        public bool PreferSharedTypes { get; set; }

        /// <summary>
        /// If enabled, will lazy load dependencies of all shared assemblies.
        /// Reduces plugin load time at the expense of non-determinism in how transitive dependencies are loaded
        /// between the plugin and the host.
        ///
        /// Please be aware of the danger of using this option:
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/pull/164#issuecomment-751557873">
        /// https://github.com/natemcmaster/DotNetCorePlugins/pull/164#issuecomment-751557873
        /// </seealso>
        /// </summary>
        public bool IsLazyLoaded { get; set; } = false;

        /// <summary>
        /// If set, replaces the default <see cref="AssemblyLoadContext"/> used by the <see cref="PluginLoader"/>.
        /// Use this feature if the <see cref="AssemblyLoadContext"/> of the <see cref="Assembly"/> is not the Runtime's default load context.
        /// i.e. (AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly) != <see cref="AssemblyLoadContext.Default"/>
        /// </summary>
        public AssemblyLoadContext DefaultContext { get; set; } = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly()) ?? AssemblyLoadContext.Default;

        private bool _isUnloadable;

        /// <summary>
        /// The plugin can be unloaded from memory.
        /// </summary>
        public bool IsUnloadable
        {
            get => _isUnloadable || EnableHotReload;
            set => _isUnloadable = value;
        }

        private bool _loadInMemory;

        /// <summary>
        /// Loads assemblies into memory in order to not lock files.
        /// As example use case here would be: no hot reloading but able to
        /// replace files and reload manually at later time
        /// </summary>
        public bool LoadInMemory
        {
            get => _loadInMemory || EnableHotReload;
            set => _loadInMemory = value;
        }

        /// <summary>
        /// When any of the loaded files changes on disk, the plugin will be reloaded.
        /// Use the event <see cref="PluginLoader.Reloaded" /> to be notified of changes.
        /// </summary>
        /// <remarks>
        /// It will load assemblies into memory in order to not lock files
        /// <see cref="LoadInMemory"/>
        /// </remarks>
        public bool EnableHotReload { get; set; }

        /// <summary>
        /// Specifies the delay to reload a plugin, after file changes have been detected.
        /// Default value is 200 milliseconds.
        /// </summary>
        public TimeSpan ReloadDelay { get; set; } = TimeSpan.FromMilliseconds(200);

        /// <summary>
        /// This will load assemblies into the default load context.
        /// This is used for integration tests. Tests run in the default load context, so we do
        /// not want type mismatch errors due to loading types in different load contexts.
        /// </summary>
        public bool LoadAssembliesInDefaultLoadContext { get; set; }
    }
}
