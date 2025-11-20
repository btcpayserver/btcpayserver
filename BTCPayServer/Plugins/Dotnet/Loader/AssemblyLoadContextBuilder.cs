#nullable enable
// Copyright (c) Nate McMaster.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.IO;
using System.Reflection;
using System.Runtime.Loader;
using BTCPayServer.Plugins.Dotnet.LibraryModel;

namespace BTCPayServer.Plugins.Dotnet.Loader
{
    /// <summary>
    /// A builder for creating an instance of <see cref="AssemblyLoadContext" />.
    /// </summary>
    public class AssemblyLoadContextBuilder
    {
        private readonly List<string> _additionalProbingPaths = new();
        private readonly List<string> _resourceProbingPaths = new();
        private readonly List<string> _resourceProbingSubpaths = new();
        private readonly Dictionary<string, ManagedLibrary> _managedLibraries = new(StringComparer.Ordinal);
        private readonly Dictionary<string, NativeLibrary> _nativeLibraries = new(StringComparer.Ordinal);
        private readonly HashSet<string> _privateAssemblies = new(StringComparer.Ordinal);
        private readonly HashSet<string> _defaultAssemblies = new(StringComparer.Ordinal);
        private AssemblyLoadContext _defaultLoadContext = AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly()) ?? AssemblyLoadContext.Default;
        private string? _mainAssemblyPath;
        private bool _preferDefaultLoadContext;
        private bool _lazyLoadReferences;

        private bool _isCollectible;
        private bool _loadInMemory;
        private bool _loadAssembliesInDefaultLoadContext;
        private bool _shadowCopyNativeLibraries;

        /// <summary>
        /// Creates an assembly load context using settings specified on the builder.
        /// </summary>
        /// <returns>A new ManagedLoadContext.</returns>
        public AssemblyLoadContext Build()
        {
            var resourceProbingPaths = new List<string>(_resourceProbingPaths);
            foreach (var additionalPath in _additionalProbingPaths)
            {
                foreach (var subPath in _resourceProbingSubpaths)
                {
                    resourceProbingPaths.Add(Path.Combine(additionalPath, subPath));
                }
            }

            if (_mainAssemblyPath == null)
            {
                throw new InvalidOperationException($"Missing required property. You must call '{nameof(SetMainAssemblyPath)}' to configure the default assembly.");
            }

            return new ManagedLoadContext(
                _mainAssemblyPath,
                _managedLibraries,
                _nativeLibraries,
                _privateAssemblies,
                _defaultAssemblies,
                _additionalProbingPaths,
                resourceProbingPaths,
                _defaultLoadContext,
                _preferDefaultLoadContext,
                _lazyLoadReferences,
                _isCollectible,
                _loadInMemory,
                _loadAssembliesInDefaultLoadContext,
                _shadowCopyNativeLibraries);
        }

        /// <summary>
        /// Set the file path to the main assembly for the context. This is used as the starting point for loading
        /// other assemblies. The directory that contains it is also known as the 'app local' directory.
        /// </summary>
        /// <param name="path">The file path. Must not be null or empty. Must be an absolute path.</param>
        /// <returns>The builder.</returns>
        public AssemblyLoadContextBuilder SetMainAssemblyPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Argument must not be null or empty.", nameof(path));
            }

            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException("Argument must be a full path.", nameof(path));
            }

            _mainAssemblyPath = path;
            return this;
        }

        /// <summary>
        /// Replaces the default <see cref="AssemblyLoadContext"/> used by the <see cref="AssemblyLoadContextBuilder"/>.
        /// Use this feature if the <see cref="AssemblyLoadContext"/> of the <see cref="Assembly"/> is not the Runtime's default load context.
        /// i.e. (AssemblyLoadContext.GetLoadContext(Assembly.GetExecutingAssembly) != <see cref="AssemblyLoadContext.Default"/>
        /// </summary>
        /// <param name="context">The context to set.</param>
        /// <returns>The builder.</returns>
        public AssemblyLoadContextBuilder SetDefaultContext(AssemblyLoadContext context)
        {
            _defaultLoadContext = context ?? throw new ArgumentException($"Bad Argument: AssemblyLoadContext in {nameof(AssemblyLoadContextBuilder)}.{nameof(SetDefaultContext)} is null.");
            return this;
        }

        /// <summary>
        /// Instructs the load context to prefer a private version of this assembly, even if that version is
        /// different from the version used by the host application.
        /// Use this when you do not need to exchange types created from within the load context with other contexts
        /// or the default app context.
        /// <para>
        /// This may mean the types loaded from
        /// this assembly will not match the types from an assembly with the same name, but different version,
        /// in the host application.
        /// </para>
        /// <para>
        /// For example, if the host application has a type named <c>Foo</c> from assembly <c>Banana, Version=1.0.0.0</c>
        /// and the load context prefers a private version of <c>Banan, Version=2.0.0.0</c>, when comparing two objects,
        /// one created by the host (Foo1) and one created from within the load context (Foo2), they will not have the same
        /// type. <c>Foo1.GetType() != Foo2.GetType()</c>
        /// </para>
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <returns>The builder.</returns>
        public AssemblyLoadContextBuilder PreferLoadContextAssembly(AssemblyName assemblyName)
        {
            if (assemblyName.Name != null)
            {
                _privateAssemblies.Add(assemblyName.Name);
            }

            return this;
        }

        /// <summary>
        /// Instructs the load context to first attempt to load assemblies by this name from the default app context, even
        /// if other assemblies in this load context express a dependency on a higher or lower version.
        /// Use this when you need to exchange types created from within the load context with other contexts
        /// or the default app context.
        /// </summary>
        /// <param name="assemblyName">The name of the assembly.</param>
        /// <returns>The builder.</returns>
        public AssemblyLoadContextBuilder PreferDefaultLoadContextAssembly(AssemblyName assemblyName)
        {
            // Lazy loaded references have dependencies resolved as they are loaded inside the actual Load Context.
            if (_lazyLoadReferences)
            {
                if (assemblyName.Name != null && !_defaultAssemblies.Contains(assemblyName.Name))
                {
                    _defaultAssemblies.Add(assemblyName.Name);
                    var assembly = _defaultLoadContext.LoadFromAssemblyName(assemblyName);
                    foreach (var reference in assembly.GetReferencedAssemblies())
                    {
                        if (reference.Name != null)
                        {
                            _defaultAssemblies.Add(reference.Name);
                        }
                    }
                }

                return this;
            }

            var names = new Queue<AssemblyName>();
            names.Enqueue(assemblyName);
            while (names.TryDequeue(out var name))
            {
                if (name.Name == null || _defaultAssemblies.Contains(name.Name))
                {
                    // base cases
                    continue;
                }

                _defaultAssemblies.Add(name.Name);

                // Load and find all dependencies of default assemblies.
                // This sacrifices some performance for determinism in how transitive
                // dependencies will be shared between host and plugin.
                var assembly = _defaultLoadContext.LoadFromAssemblyName(name);

                foreach (var reference in assembly.GetReferencedAssemblies())
                {
                    names.Enqueue(reference);
                }
            }

            return this;
        }

        /// <summary>
        /// Instructs the load context to first search for binaries from the default app context, even
        /// if other assemblies in this load context express a dependency on a higher or lower version.
        /// Use this when you need to exchange types created from within the load context with other contexts
        /// or the default app context.
        /// <para>
        /// This may mean the types loaded from within the context are force-downgraded to the version provided
        /// by the host. <seealso cref="PreferLoadContextAssembly" /> can be used to selectively identify binaries
        /// which should not be loaded from the default load context.
        /// </para>
        /// </summary>
        /// <param name="preferDefaultLoadContext">When true, first attemp to load binaries from the default load context.</param>
        /// <returns>The builder.</returns>
        public AssemblyLoadContextBuilder PreferDefaultLoadContext(bool preferDefaultLoadContext)
        {
            _preferDefaultLoadContext = preferDefaultLoadContext;
            return this;
        }

        /// <summary>
        /// Instructs the load context to lazy load dependencies of all shared assemblies.
        /// Reduces plugin load time at the expense of non-determinism in how transitive dependencies are loaded
        /// between the plugin and the host.
        ///
        /// Please be aware of the danger of using this option:
        /// <seealso href="https://github.com/natemcmaster/DotNetCorePlugins/pull/164#issuecomment-751557873">
        /// https://github.com/natemcmaster/DotNetCorePlugins/pull/164#issuecomment-751557873
        /// </seealso>
        /// </summary>
        /// <param name="isLazyLoaded">True to lazy load, else false.</param>
        /// <returns>The builder.</returns>
        public AssemblyLoadContextBuilder IsLazyLoaded(bool isLazyLoaded)
        {
            _lazyLoadReferences = isLazyLoaded;
            return this;
        }

        /// <summary>
        /// Add a managed library to the load context.
        /// </summary>
        /// <param name="library">The managed library.</param>
        /// <returns>The builder.</returns>
        public AssemblyLoadContextBuilder AddManagedLibrary(ManagedLibrary library)
        {
            ValidateRelativePath(library.AdditionalProbingPath);

            if (library.Name.Name != null)
            {
                _managedLibraries.Add(library.Name.Name, library);
            }

            return this;
        }

        /// <summary>
        /// Add a native library to the load context.
        /// </summary>
        /// <param name="library"></param>
        /// <returns></returns>
        public AssemblyLoadContextBuilder AddNativeLibrary(NativeLibrary library)
        {
            ValidateRelativePath(library.AppLocalPath);
            ValidateRelativePath(library.AdditionalProbingPath);

            _nativeLibraries.Add(library.Name, library);
            return this;
        }

        /// <summary>
        /// Add a <paramref name="path"/> that should be used to search for native and managed libraries.
        /// </summary>
        /// <param name="path">The file path. Must be a full file path.</param>
        /// <returns>The builder</returns>
        public AssemblyLoadContextBuilder AddProbingPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Value must not be null or empty.", nameof(path));
            }

            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException("Argument must be a full path.", nameof(path));
            }

            _additionalProbingPaths.Add(path);
            return this;
        }

        /// <summary>
        /// Add a <paramref name="path"/> that should be use to search for resource assemblies (aka satellite assemblies).
        /// </summary>
        /// <param name="path">The file path. Must be a full file path.</param>
        /// <returns>The builder</returns>
        public AssemblyLoadContextBuilder AddResourceProbingPath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Value must not be null or empty.", nameof(path));
            }

            if (!Path.IsPathRooted(path))
            {
                throw new ArgumentException("Argument must be a full path.", nameof(path));
            }

            _resourceProbingPaths.Add(path);
            return this;
        }

        /// <summary>
        /// Enable unloading the assembly load context.
        /// </summary>
        /// <returns>The builder</returns>
        public AssemblyLoadContextBuilder EnableUnloading()
        {
            _isCollectible = true;
            return this;
        }

        /// <summary>
        /// Read .dll files into memory to avoid locking the files.
        /// This is not as efficient, so is not enabled by default, but is required for scenarios
        /// like hot reloading.
        /// </summary>
        /// <returns>The builder</returns>
        public AssemblyLoadContextBuilder PreloadAssembliesIntoMemory()
        {
            _loadInMemory = true; // required to prevent dotnet from locking loaded files
            return this;
        }

        /// <summary>
        /// This will load assemblies into the default load context.
        /// This is used for integration tests. Tests run in the default load context, so we do
        /// not want type mismatch errors due to loading types in different load contexts.
        /// </summary>
        /// <returns></returns>
        public AssemblyLoadContextBuilder LoadAssembliesInDefaultLoadContext()
        {
            _loadAssembliesInDefaultLoadContext = true;
            return this;
        }

        /// <summary>
        /// Shadow copy native libraries (unmanaged DLLs) to avoid locking of these files.
        /// This is not as efficient, so is not enabled by default, but is required for scenarios
        /// like hot reloading of plugins dependent on native libraries.
        /// </summary>
        /// <returns>The builder</returns>
        public AssemblyLoadContextBuilder ShadowCopyNativeLibraries()
        {
            _shadowCopyNativeLibraries = true;
            return this;
        }

        /// <summary>
        /// Add a <paramref name="path"/> that should be use to search for resource assemblies (aka satellite assemblies)
        /// relative to any paths specified as <see cref="AddProbingPath"/>
        /// </summary>
        /// <param name="path">The file path. Must not be a full file path since it will be appended to additional probing path roots.</param>
        /// <returns>The builder</returns>
        internal AssemblyLoadContextBuilder AddResourceProbingSubpath(string path)
        {
            if (string.IsNullOrEmpty(path))
            {
                throw new ArgumentException("Value must not be null or empty.", nameof(path));
            }

            if (Path.IsPathRooted(path))
            {
                throw new ArgumentException("Argument must be not a full path.", nameof(path));
            }

            _resourceProbingSubpaths.Add(path);
            return this;
        }

        private static void ValidateRelativePath(string probingPath)
        {
            if (string.IsNullOrEmpty(probingPath))
            {
                throw new ArgumentException("Value must not be null or empty.", nameof(probingPath));
            }

            if (Path.IsPathRooted(probingPath))
            {
                throw new ArgumentException("Argument must be a relative path.", nameof(probingPath));
            }
        }
    }
}
