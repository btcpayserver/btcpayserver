#nullable enable
using System;
using System.Collections.Generic;
using System.Globalization;
using System.Net;
using System.Net.Sockets;

namespace BTCPayServer;

internal sealed record SSRFAllowedDestination(string? Hostname, IPNetwork? Network, int? Port)
{
    public static IReadOnlyList<SSRFAllowedDestination> ParseList(string? value)
    {
        if (string.IsNullOrWhiteSpace(value))
            return [];

        var result = new List<SSRFAllowedDestination>();
        var unique = new HashSet<SSRFAllowedDestination>();
        foreach (var entry in value.Split([',', ';'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            var destination = Parse(entry);
            if (unique.Add(destination))
                result.Add(destination);
        }
        return result;
    }

    public static SSRFAllowedDestination Parse(string value)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(value);
        value = value.Trim();
        var bracketed = value.StartsWith('[');
        string destination;
        int? port = null;

        if (bracketed)
        {
            var closingBracket = value.IndexOf(']');
            if (closingBracket <= 1)
                throw Invalid(value);
            destination = value[1..closingBracket];
            var suffix = value[(closingBracket + 1)..];
            if (suffix.Length is not 0)
            {
                if (!suffix.StartsWith(':'))
                    throw Invalid(value);
                port = ParsePort(suffix[1..], value);
            }
        }
        else
        {
            if (value.Contains('[') || value.Contains(']'))
                throw Invalid(value);
            var firstColon = value.IndexOf(':');
            if (firstColon >= 0 && firstColon == value.LastIndexOf(':'))
            {
                destination = value[..firstColon];
                port = ParsePort(value[(firstColon + 1)..], value);
            }
            else
            {
                destination = value;
            }
        }

        if (destination.Contains('/'))
        {
            var slash = destination.LastIndexOf('/');
            if (!IPAddress.TryParse(destination[..slash], out var configuredAddress) ||
                !IPNetwork.TryParse(destination, out var network) ||
                !configuredAddress.Equals(network.BaseAddress))
                throw Invalid(value);
            network = Normalize(network, value);
            if (network.PrefixLength is 0)
                throw Invalid(value);
            return new SSRFAllowedDestination(null, network, port);
        }

        if (IPAddress.TryParse(destination, out var address))
        {
            if (address.AddressFamily is AddressFamily.InterNetworkV6 && address.ScopeId is not 0)
                throw Invalid(value);
            address = Normalize(address);
            var prefixLength = address.AddressFamily is AddressFamily.InterNetwork ? 32 : 128;
            return new SSRFAllowedDestination(null, new IPNetwork(address, prefixLength), port);
        }

        if (bracketed)
            throw Invalid(value);
        return new SSRFAllowedDestination(NormalizeHostname(destination, value), null, port);
    }

    public bool MatchesHostname(string hostname, int port)
    {
        if (Hostname is null || !MatchesPort(port))
            return false;
        try
        {
            return StringComparer.OrdinalIgnoreCase.Equals(Hostname, NormalizeHostname(hostname, hostname));
        }
        catch (FormatException)
        {
            return false;
        }
    }

    public bool MatchesAddress(IPAddress address, int port)
    {
        return Network is not null && MatchesPort(port) && Network.Value.Contains(Normalize(address));
    }

    private bool MatchesPort(int port) => Port is null || Port == port;

    private static int ParsePort(string value, string entry)
    {
        if (!int.TryParse(value, NumberStyles.None, CultureInfo.InvariantCulture, out var port) || port is < 1 or > 65535)
            throw Invalid(entry);
        return port;
    }

    private static IPNetwork Normalize(IPNetwork network, string entry)
    {
        var address = network.BaseAddress;
        if (address.AddressFamily is AddressFamily.InterNetworkV6 && address.ScopeId is not 0)
            throw Invalid(entry);
        if (!address.IsIPv4MappedToIPv6)
            return network;
        if (network.PrefixLength < 96)
            throw Invalid(entry);
        return new IPNetwork(address.MapToIPv4(), network.PrefixLength - 96);
    }

    private static IPAddress Normalize(IPAddress address)
    {
        return address.IsIPv4MappedToIPv6 ? address.MapToIPv4() : address;
    }

    private static string NormalizeHostname(string hostname, string entry)
    {
        if (hostname.EndsWith(".", StringComparison.Ordinal))
            hostname = hostname[..^1];
        if (hostname.Length is 0 || hostname.EndsWith(".", StringComparison.Ordinal))
            throw Invalid(entry);
        try
        {
            hostname = new IdnMapping().GetAscii(hostname);
        }
        catch (ArgumentException)
        {
            throw Invalid(entry);
        }
        if (Uri.CheckHostName(hostname) is not UriHostNameType.Dns)
            throw Invalid(entry);
        return hostname.ToLowerInvariant();
    }

    private static FormatException Invalid(string value) => new($"Invalid SSRF exception '{value}'");
}
