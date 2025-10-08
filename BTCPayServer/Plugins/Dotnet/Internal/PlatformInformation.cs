// Copyright (c) Nate McMaster.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace BTCPayServer.Plugins.Dotnet.Internal
{
    internal class PlatformInformation
    {
        public static readonly string[] NativeLibraryExtensions;
        public static readonly string[] NativeLibraryPrefixes;
        public static readonly string[] ManagedAssemblyExtensions = new[]
        {
                ".dll",
                ".ni.dll",
                ".exe",
                ".ni.exe"
        };

        static PlatformInformation()
        {
            if (RuntimeInformation.IsOSPlatform(OSPlatform.Windows))
            {
                NativeLibraryPrefixes = new[] { "" };
                NativeLibraryExtensions = new[] { ".dll" };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.OSX))
            {
                NativeLibraryPrefixes = new[] { "", "lib", };
                NativeLibraryExtensions = new[] { ".dylib" };
            }
            else if (RuntimeInformation.IsOSPlatform(OSPlatform.Linux))
            {
                NativeLibraryPrefixes = new[] { "", "lib" };
                NativeLibraryExtensions = new[] { ".so", ".so.1" };
            }
            else
            {
                Debug.Fail("Unknown OS type");
                NativeLibraryPrefixes = Array.Empty<string>();
                NativeLibraryExtensions = Array.Empty<string>();
            }
        }
    }
}
