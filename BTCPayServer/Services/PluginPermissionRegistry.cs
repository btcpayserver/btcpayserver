using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;
using BTCPayServer.Client;

namespace BTCPayServer.Services;

public class PluginPermissionRegistry
{
    private readonly IReadOnlyDictionary<string, PluginPermission> _permissions;
    private readonly IReadOnlyDictionary<string, HashSet<string>> _pluginPolicyMap;
    private static readonly CultureInfo Culture = new(CultureInfo.InvariantCulture.Name);

    public PluginPermissionRegistry(IEnumerable<PluginPermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var permissionMap = new Dictionary<string, PluginPermission>(StringComparer.Ordinal);
        var pluginPolicyMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var permission in permissions)
        {
            if (permission is null)
                continue;

            ValidatePermission(permission);
            permissionMap[permission.Policy] = permission;

            if (permission.ChildPolicies is { Count: > 0 })
                pluginPolicyMap[permission.Policy] = permission.ChildPolicies.ToHashSet(StringComparer.Ordinal);
        }

        _permissions = new ReadOnlyDictionary<string, PluginPermission>(permissionMap);
        _pluginPolicyMap = new ReadOnlyDictionary<string, HashSet<string>>(pluginPolicyMap);
    }

    private static void ValidatePermission(PluginPermission permission)
    {
        if (string.IsNullOrEmpty(permission.Policy))
            throw new ArgumentException("Permission policy cannot be null or empty");

        if (!permission.Policy.StartsWith("btcpay.plugin.", StringComparison.Ordinal))
            throw new ArgumentException($"Plugin permission policy must start with 'btcpay.plugin.' (case-sensitive): {permission.Policy}");

        if (string.IsNullOrEmpty(permission.DisplayName))
            throw new ArgumentException($"Permission display name cannot be null or empty for policy: {permission.Policy}");

        if (string.IsNullOrEmpty(permission.PluginIdentifier))
            throw new ArgumentException($"Permission plugin identifier cannot be null or empty for policy: {permission.Policy}");
    }

    public IEnumerable<PluginPermission> GetAllPluginPermissions()
    {
        return _permissions.Values;
    }

    public PluginPermission GetPermission(string policy)
    {
        if (string.IsNullOrEmpty(policy))
            return null;

        return _permissions.TryGetValue(policy, out var permission) ? permission : null;
    }

    public Dictionary<string, HashSet<string>> GetPluginPolicyMap()
    {
        return _pluginPolicyMap.ToDictionary(
            kvp => kvp.Key,
            kvp => new HashSet<string>(kvp.Value, StringComparer.Ordinal),
            StringComparer.Ordinal);
    }

    public bool IsRegisteredPluginPermission(string policy)
    {
        return !string.IsNullOrEmpty(policy) && _permissions.ContainsKey(policy);
    }

    public string GetDisplayName(string policy)
    {
        if (string.IsNullOrEmpty(policy))
            return policy;

        if (GetPermission(policy)?.DisplayName is { Length: > 0 } displayName)
            return displayName;

        if (Policies.IsPluginPolicy(policy))
        {
            var parts = policy.Split('.');
            var permissionName = parts.Length > 2 ? string.Join(' ', parts[2..]) : policy;
            return $"⚠️ [Uninstalled Plugin] {Culture.TextInfo.ToTitleCase(permissionName)}";
        }

        return Policies.DisplayName(policy);
    }
}
