// Copyright (c) Nate McMaster.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.IO;
using System.Reflection;

namespace BTCPayServer.Plugins.Dotnet.LibraryModel
{
    /// <summary>
    /// Represents a managed, .NET assembly.
    /// </summary>
    [DebuggerDisplay("{Name} = {AdditionalProbingPath}")]
    public class ManagedLibrary
    {
        private ManagedLibrary(AssemblyName name, string additionalProbingPath, string appLocalPath)
        {
            Name = name ?? throw new ArgumentNullException(nameof(name));
            AdditionalProbingPath = additionalProbingPath ?? throw new ArgumentNullException(nameof(additionalProbingPath));
            AppLocalPath = appLocalPath ?? throw new ArgumentNullException(nameof(appLocalPath));
        }

        /// <summary>
        /// Name of the managed library
        /// </summary>
        public AssemblyName Name { get; private set; }

        /// <summary>
        /// Contains path to file within an additional probing path root. This is typically a combination
        /// of the NuGet package ID (lowercased), version, and path within the package.
        /// <para>
        /// For example, <c>microsoft.data.sqlite/1.0.0/lib/netstandard1.3/Microsoft.Data.Sqlite.dll</c>
        /// </para>
        /// </summary>
        public string AdditionalProbingPath { get; private set; }

        /// <summary>
        /// Contains path to file within a deployed, framework-dependent application.
        /// <para>
        /// For most managed libraries, this will be the file name.
        /// For example, <c>MyPlugin1.dll</c>.
        /// </para>
        /// <para>
        /// For runtime-specific managed implementations, this may include a sub folder path.
        /// For example, <c>runtimes/win/lib/netcoreapp2.0/System.Diagnostics.EventLog.dll</c>
        /// </para>
        /// </summary>
        public string AppLocalPath { get; private set; }

        /// <summary>
        /// Create an instance of <see cref="ManagedLibrary" /> from a NuGet package.
        /// </summary>
        /// <param name="packageId">The name of the package.</param>
        /// <param name="packageVersion">The version of the package.</param>
        /// <param name="assetPath">The path within the NuGet package.</param>
        /// <returns></returns>
        public static ManagedLibrary CreateFromPackage(string packageId, string packageVersion, string assetPath)
        {
            // When the asset comes from "lib/$tfm/", Microsoft.NET.Sdk will flatten this during publish based on the most compatible TFM.
            // The SDK will not flatten managed libraries found under runtimes/
            var appLocalPath = assetPath.StartsWith("lib/")
                ? Path.GetFileName(assetPath)
                : assetPath;

            return new ManagedLibrary(
                new AssemblyName(Path.GetFileNameWithoutExtension(assetPath)),
                Path.Combine(packageId.ToLowerInvariant(), packageVersion, assetPath),
                appLocalPath
            );
        }
    }
}
