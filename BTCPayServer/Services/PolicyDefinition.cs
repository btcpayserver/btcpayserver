#nullable enable
using System;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Client;

namespace BTCPayServer.Services;

public class PermissionDisplay(string title, string description)
{
    public string Title { get; } = title ?? throw new ArgumentNullException(nameof(title));
    public string Description { get; } = description ?? throw new ArgumentNullException(nameof(description));
}

public class PolicyDefinition
{
    public PolicyDefinition(string policy,
        PermissionDisplay display,
        PermissionDisplay? scopeDisplay = null,
        IEnumerable<string>? includedPermissions = null,
        IEnumerable<string>? includedByPermissions = null)
    {
        Policy = policy switch
        {
            null => throw new ArgumentNullException(nameof(policy)),
            _ when Permission.Parse(policy) is { Scope: null } p => p.Policy,
            _ => throw new ArgumentException("Invalid policy (this should be a permission without scope)", nameof(policy))
        };
        Type = Permission.TryGetPolicyType(Policy);
        Display = display ?? throw new ArgumentNullException(nameof(display));
        ScopeDisplay = scopeDisplay;
        IncludedPermissions = includedPermissions?.ToArray() ?? Array.Empty<string>();
        IncludedByPermissions = includedByPermissions?.ToArray() ?? Array.Empty<string>();
    }

    public PolicyType? Type { get; }

    public string Policy { get; }

    public PermissionDisplay Display { get; }
    public PermissionDisplay? ScopeDisplay { get; }
    public IReadOnlyCollection<string> IncludedPermissions { get; }
    public IReadOnlyCollection<string> IncludedByPermissions { get; }
    public override string ToString() => Policy;
}

