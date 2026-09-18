#nullable enable
using System;
using System.Collections.Frozen;
using System.Collections.Immutable;
using System.Threading;

namespace BTCPayServer.Plugins.Maintenance;

public sealed record HostIntegrationSnapshot(
    bool Available,
    FrozenSet<string> SupportedCommands,
    HostEnvironmentSnapshot? Environment)
{
    public static HostIntegrationSnapshot Unavailable { get; } =
        new(false, FrozenSet<string>.Empty, null);
}

public sealed record HostEnvironmentSnapshot(
    string? DeploymentType,
    ImmutableArray<string> Commands,
    HostRoutesSnapshot? Routes)
{
    internal static HostEnvironmentSnapshot? From(BTCPayHostEnvironment? environment)
    {
        if (environment is null)
            return null;

        var routes = environment.Routes is null
            ? null
            : new HostRoutesSnapshot(
                [..environment.Routes.OptionalRoutes ?? []],
                [..environment.Routes.EnabledRoutes ?? []]);
        return new HostEnvironmentSnapshot(
            environment.DeploymentType,
            [..environment.Commands ?? []],
            routes);
    }
}

public sealed record HostRoutesSnapshot(
    ImmutableArray<string> OptionalRoutes,
    ImmutableArray<string> EnabledRoutes);

public sealed class HostIntegrationState
{
    private HostIntegrationSnapshot _current = HostIntegrationSnapshot.Unavailable;

    public HostIntegrationSnapshot Current => Volatile.Read(ref _current);

    internal void Update(HostIntegrationSnapshot snapshot)
    {
        ArgumentNullException.ThrowIfNull(snapshot);
        Volatile.Write(ref _current, snapshot);
    }
}
