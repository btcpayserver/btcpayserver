#nullable enable
// Copyright (c) Nate McMaster.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

namespace BTCPayServer.Plugins.Dotnet.Internal
{
    internal class RuntimeOptions
    {
        public string? Tfm { get; set; }

        public string[]? AdditionalProbingPaths { get; set; }
    }
}
