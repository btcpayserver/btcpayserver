#nullable enable
using System;

namespace BTCPayServer.Abstractions.Extensions;

public static class NetworkExtensions
{
    public static bool IsOnionHost(string? host)
        => host is not null &&
           Uri.CheckHostName(host) == UriHostNameType.Dns &&
           host.TrimEnd('.').EndsWith(".onion", StringComparison.OrdinalIgnoreCase);
}
