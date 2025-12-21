#nullable enable
// Copyright (c) Nate McMaster.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.Loader;
using BTCPayServer.Plugins.Dotnet.Internal;
using BTCPayServer.Plugins.Dotnet.LibraryModel;

namespace BTCPayServer.Plugins.Dotnet.Loader
{
    /// <summary>
    /// An implementation of <see cref="AssemblyLoadContext" /> which attempts to load managed and native
    /// binaries at runtime immitating some of the behaviors of corehost.
    /// </summary>
    [DebuggerDisplay("'{Name}' ({_mainAssemblyPath})")]
    internal class ManagedLoadContext : AssemblyLoadContext
    {
        private readonly string _basePath;
        private readonly string _mainAssemblyPath;
        private readonly IReadOnlyDictionary<string, ManagedLibrary> _managedAssemblies;
        private readonly IReadOnlyDictionary<string, NativeLibrary> _nativeLibraries;
        private readonly IReadOnlyCollection<string> _privateAssemblies;
        private readonly ICollection<string> _defaultAssemblies;
        private readonly IReadOnlyCollection<string> _additionalProbingPaths;
        private readonly bool _preferDefaultLoadContext;
        private readonly string[] _resourceRoots;
        private readonly bool _loadInMemory;
        private readonly bool _loadAssembliesInDefaultLoadContext;
        private readonly bool _lazyLoadReferences;
        private readonly List<AssemblyLoadContext> _assemblyLoadContexts = new();
        private readonly AssemblyDependencyResolver _dependencyResolver;
        private readonly bool _shadowCopyNativeLibraries;
        private readonly string _unmanagedDllShadowCopyDirectoryPath;

        public ManagedLoadContext(string mainAssemblyPath,
            IReadOnlyDictionary<string, ManagedLibrary> managedAssemblies,
            IReadOnlyDictionary<string, NativeLibrary> nativeLibraries,
            IReadOnlyCollection<string> privateAssemblies,
            IReadOnlyCollection<string> defaultAssemblies,
            IReadOnlyCollection<string> additionalProbingPaths,
            IReadOnlyCollection<string> resourceProbingPaths,
            AssemblyLoadContext defaultLoadContext,
            bool preferDefaultLoadContext,
            bool lazyLoadReferences,
            bool isCollectible,
            bool loadInMemory,
            bool loadAssembliesInDefaultLoadContext,
            bool shadowCopyNativeLibraries)
            : base(Path.GetFileNameWithoutExtension(mainAssemblyPath), isCollectible)
        {
            if (resourceProbingPaths == null)
            {
                throw new ArgumentNullException(nameof(resourceProbingPaths));
            }

            _mainAssemblyPath = mainAssemblyPath ?? throw new ArgumentNullException(nameof(mainAssemblyPath));
            _dependencyResolver = new AssemblyDependencyResolver(mainAssemblyPath);
            _basePath = Path.GetDirectoryName(mainAssemblyPath) ?? throw new ArgumentException(nameof(mainAssemblyPath));
            _managedAssemblies = managedAssemblies ?? throw new ArgumentNullException(nameof(managedAssemblies));
            _privateAssemblies = privateAssemblies ?? throw new ArgumentNullException(nameof(privateAssemblies));
            _defaultAssemblies = defaultAssemblies != null ? defaultAssemblies.ToList() : throw new ArgumentNullException(nameof(defaultAssemblies));
            _nativeLibraries = nativeLibraries ?? throw new ArgumentNullException(nameof(nativeLibraries));
            _additionalProbingPaths = additionalProbingPaths ?? throw new ArgumentNullException(nameof(additionalProbingPaths));
            _assemblyLoadContexts.Add(defaultLoadContext);
            _preferDefaultLoadContext = preferDefaultLoadContext;
            _loadAssembliesInDefaultLoadContext = loadAssembliesInDefaultLoadContext;
            _loadInMemory = loadInMemory;
            _lazyLoadReferences = lazyLoadReferences;

            _resourceRoots = new[] { _basePath }
                .Concat(resourceProbingPaths)
                .ToArray();

            _shadowCopyNativeLibraries = shadowCopyNativeLibraries;
            _unmanagedDllShadowCopyDirectoryPath = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName());

            if (shadowCopyNativeLibraries)
            {
                Unloading += _ => OnUnloaded();
            }
        }

        public void AddAssemblyLoadContexts(IEnumerable<AssemblyLoadContext> assemblyLoadContexts) => _assemblyLoadContexts.AddRange(assemblyLoadContexts);

        /// <summary>
        /// Load an assembly.
        /// </summary>
        /// <param name="assemblyName"></param>
        /// <returns></returns>
        protected override Assembly? Load(AssemblyName assemblyName)
        {
            if (assemblyName.Name == null)
            {
                // not sure how to handle this case. It's technically possible.
                return null;
            }

            if ((_preferDefaultLoadContext || _defaultAssemblies.Contains(assemblyName.Name)) && !_privateAssemblies.Contains(assemblyName.Name))
            {
                var name = new AssemblyName(assemblyName.Name);
                var assembly = _assemblyLoadContexts.Select(p => TryLoadFromAssemblyName(p, name)).FirstOrDefault(a => a is not null);
                if (assembly is not null)
                    return assembly;
            }

            var resolvedPath = _dependencyResolver.ResolveAssemblyToPath(assemblyName);
            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
            {
                return LoadAssemblyFromFilePath(resolvedPath);
            }

            // Resource assembly binding does not use the TPA. Instead, it probes PLATFORM_RESOURCE_ROOTS (a list of folders)
            // for $folder/$culture/$assemblyName.dll
            // See https://github.com/dotnet/coreclr/blob/3fca50a36e62a7433d7601d805d38de6baee7951/src/binder/assemblybinder.cpp#L1232-L1290

            if (!string.IsNullOrEmpty(assemblyName.CultureName) && !string.Equals("neutral", assemblyName.CultureName))
            {
                foreach (var resourceRoot in _resourceRoots)
                {
                    var resourcePath = Path.Combine(resourceRoot, assemblyName.CultureName, assemblyName.Name + ".dll");
                    if (File.Exists(resourcePath))
                    {
                        return LoadAssemblyFromFilePath(resourcePath);
                    }
                }

                return null;
            }

            if (_managedAssemblies.TryGetValue(assemblyName.Name, out var library) && library != null)
            {
                if (SearchForLibrary(library, out var path) && path != null)
                {
                    return LoadAssemblyFromFilePath(path);
                }
            }
            else
            {
                // if an assembly was not listed in the list of known assemblies,
                // fallback to the load context base directory
                var dllName = assemblyName.Name + ".dll";
                foreach (var probingPath in _additionalProbingPaths.Prepend(_basePath))
                {
                    var localFile = Path.Combine(probingPath, dllName);
                    if (File.Exists(localFile))
                    {
                        return LoadAssemblyFromFilePath(localFile);
                    }
                }
            }

            return null;
        }

        private Assembly? TryLoadFromAssemblyName(AssemblyLoadContext context, AssemblyName name)
        {
            // If default context is preferred, check first for types in the default context unless the dependency has been declared as private
            try
            {
                var defaultAssembly = context.LoadFromAssemblyName(name);
                if (defaultAssembly != null)
                {
                    // Add referenced assemblies to the list of default assemblies.
                    // This is basically lazy loading
                    if (_lazyLoadReferences)
                    {
                        foreach (var reference in defaultAssembly.GetReferencedAssemblies())
                        {
                            if (reference.Name != null && !_defaultAssemblies.Contains(reference.Name))
                            {
                                _defaultAssemblies.Add(reference.Name);
                            }
                        }
                    }

                    // Older versions used to return null here such that returned assembly would be resolved from the default ALC.
                    // However, with the addition of custom default ALCs, the Default ALC may not be the user's chosen ALC when
                    // this context was built. As such, we simply return the Assembly from the user's chosen default load context.
                    return defaultAssembly;
                }
            }
            catch
            {
                // Swallow errors in loading from the default context
            }

            return null;
        }

        private AssemblyLoadContext LoadContext => _loadAssembliesInDefaultLoadContext ? Default : this;
        public Assembly LoadAssemblyFromFilePath(string path)
        {
            if (!_loadInMemory)
            {
                return LoadContext.LoadFromAssemblyPath(path);
            }

            using var file = File.Open(path, FileMode.Open, FileAccess.Read, FileShare.Read);
            var pdbPath = Path.ChangeExtension(path, ".pdb");
            if (File.Exists(pdbPath))
            {
                using var pdbFile = File.Open(pdbPath, FileMode.Open, FileAccess.Read, FileShare.Read);
                return LoadContext.LoadFromStream(file, pdbFile);
            }
            return LoadContext.LoadFromStream(file);

        }

        /// <summary>
        /// Loads the unmanaged binary using configured list of native libraries.
        /// </summary>
        /// <param name="unmanagedDllName"></param>
        /// <returns></returns>
        protected override IntPtr LoadUnmanagedDll(string unmanagedDllName)
        {
            var resolvedPath = _dependencyResolver.ResolveUnmanagedDllToPath(unmanagedDllName);
            if (!string.IsNullOrEmpty(resolvedPath) && File.Exists(resolvedPath))
            {
                return LoadUnmanagedDllFromResolvedPath(resolvedPath, normalizePath: false);
            }

            foreach (var prefix in PlatformInformation.NativeLibraryPrefixes)
            {
                if (_nativeLibraries.TryGetValue(prefix + unmanagedDllName, out var library))
                {
                    if (SearchForLibrary(library, prefix, out var path) && path != null)
                    {
                        return LoadUnmanagedDllFromResolvedPath(path);
                    }
                }
                else
                {
                    // coreclr allows code to use [DllImport("sni")] or [DllImport("sni.dll")]
                    // This library treats the file name without the extension as the lookup name,
                    // so this loop is necessary to check if the unmanaged name matches a library
                    // when the file extension has been trimmed.
                    foreach (var suffix in PlatformInformation.NativeLibraryExtensions)
                    {
                        if (!unmanagedDllName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))
                        {
                            continue;
                        }

                        // check to see if there is a library entry for the library without the file extension
                        var trimmedName = unmanagedDllName.Substring(0, unmanagedDllName.Length - suffix.Length);

                        if (_nativeLibraries.TryGetValue(prefix + trimmedName, out library))
                        {
                            if (SearchForLibrary(library, prefix, out var path) && path != null)
                            {
                                return LoadUnmanagedDllFromResolvedPath(path);
                            }
                        }
                        else
                        {
                            // fallback to native assets which match the file name in the plugin base directory
                            var prefixSuffixDllName = prefix + unmanagedDllName + suffix;
                            var prefixDllName = prefix + unmanagedDllName;

                            foreach (var probingPath in _additionalProbingPaths.Prepend(_basePath))
                            {
                                var localFile = Path.Combine(probingPath, prefixSuffixDllName);
                                if (File.Exists(localFile))
                                {
                                    return LoadUnmanagedDllFromResolvedPath(localFile);
                                }

                                var localFileWithoutSuffix = Path.Combine(probingPath, prefixDllName);
                                if (File.Exists(localFileWithoutSuffix))
                                {
                                    return LoadUnmanagedDllFromResolvedPath(localFileWithoutSuffix);
                                }
                            }

                        }
                    }

                }
            }

            return base.LoadUnmanagedDll(unmanagedDllName);
        }

        private bool SearchForLibrary(ManagedLibrary library, out string? path)
        {
            // 1. Check for in _basePath + app local path
            var localFile = Path.Combine(_basePath, library.AppLocalPath);
            if (File.Exists(localFile))
            {
                path = localFile;
                return true;
            }

            // 2. Search additional probing paths
            foreach (var searchPath in _additionalProbingPaths)
            {
                var candidate = Path.Combine(searchPath, library.AdditionalProbingPath);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }

            // 3. Search in base path
            foreach (var ext in PlatformInformation.ManagedAssemblyExtensions)
            {
                var local = Path.Combine(_basePath, library.Name.Name + ext);
                if (File.Exists(local))
                {
                    path = local;
                    return true;
                }
            }

            path = null;
            return false;
        }

        private bool SearchForLibrary(NativeLibrary library, string prefix, out string? path)
        {
            // 1. Search in base path
            foreach (var ext in PlatformInformation.NativeLibraryExtensions)
            {
                var candidate = Path.Combine(_basePath, $"{prefix}{library.Name}{ext}");
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }

            // 2. Search in base path + app local (for portable deployments of netcoreapp)
            var local = Path.Combine(_basePath, library.AppLocalPath);
            if (File.Exists(local))
            {
                path = local;
                return true;
            }

            // 3. Search additional probing paths
            foreach (var searchPath in _additionalProbingPaths)
            {
                var candidate = Path.Combine(searchPath, library.AdditionalProbingPath);
                if (File.Exists(candidate))
                {
                    path = candidate;
                    return true;
                }
            }

            path = null;
            return false;
        }

        private IntPtr LoadUnmanagedDllFromResolvedPath(string unmanagedDllPath, bool normalizePath = true)
        {
            if (normalizePath)
            {
                unmanagedDllPath = Path.GetFullPath(unmanagedDllPath);
            }

            return _shadowCopyNativeLibraries
                ? LoadUnmanagedDllFromShadowCopy(unmanagedDllPath)
                : LoadUnmanagedDllFromPath(unmanagedDllPath);
        }

        private IntPtr LoadUnmanagedDllFromShadowCopy(string unmanagedDllPath)
        {
            var shadowCopyDllPath = CreateShadowCopy(unmanagedDllPath);

            return LoadUnmanagedDllFromPath(shadowCopyDllPath);
        }

        private string CreateShadowCopy(string dllPath)
        {
            Directory.CreateDirectory(_unmanagedDllShadowCopyDirectoryPath);

            var dllFileName = Path.GetFileName(dllPath);
            var shadowCopyPath = Path.Combine(_unmanagedDllShadowCopyDirectoryPath, dllFileName);

            if (!File.Exists(shadowCopyPath))
            {
                File.Copy(dllPath, shadowCopyPath);
            }

            return shadowCopyPath;
        }

        private void OnUnloaded()
        {
            if (!_shadowCopyNativeLibraries || !Directory.Exists(_unmanagedDllShadowCopyDirectoryPath))
            {
                return;
            }

            // Attempt to delete shadow copies
            try
            {
                Directory.Delete(_unmanagedDllShadowCopyDirectoryPath, recursive: true);
            }
            catch (Exception)
            {
                // Files might be locked by host process. Nothing we can do about it, I guess.
            }
        }
    }
}
