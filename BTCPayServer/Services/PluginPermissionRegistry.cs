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
    private readonly IReadOnlyList<PluginPermission> _orderedPermissions;
    private readonly Dictionary<string, HashSet<string>> _childMap;
    private static readonly CultureInfo _culture = new(CultureInfo.InvariantCulture.Name);

    public PluginPermissionRegistry(IEnumerable<PluginPermission> permissions)
    {
        ArgumentNullException.ThrowIfNull(permissions);

        var map = new Dictionary<string, PluginPermission>(StringComparer.Ordinal);
        _childMap = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);

        foreach (var permission in permissions)
        {
            if (permission is null)
                continue;
            // Circular ChildPolicies will cause a StackOverflowException here.
            // If you're a plugin author and you hit that - congrats - you defined circular dependencies. Fix your tree.
            FlattenTree(permission, map);
        }

        _permissions = new ReadOnlyDictionary<string, PluginPermission>(map);
        _orderedPermissions = new ReadOnlyCollection<PluginPermission>(
            map.Values.OrderBy(p => p.Policy, StringComparer.Ordinal).ToList());
    }

    private void FlattenTree(PluginPermission permission, Dictionary<string, PluginPermission> map)
    {
        if (!map.TryAdd(permission.Policy, permission))
            return; // already visited

        if (!_childMap.ContainsKey(permission.Policy))
            _childMap[permission.Policy] = new HashSet<string>(StringComparer.Ordinal);

        foreach (var child in permission.ChildPolicies)
        {
            _childMap[permission.Policy].Add(child.Policy);
            FlattenTree(child, map);
        }
    }

    public IEnumerable<PluginPermission> GetAllPluginPermissions() => _orderedPermissions;

    public PluginPermission GetPermission(string policy)
    {
        if (string.IsNullOrEmpty(policy))
            return null;
        return _permissions.TryGetValue(policy, out var p) ? p : null;
    }

    // Returns true if any granted policy equals or transitively implies the required policy via ChildPolicies hierarchy.
    public bool IsSatisfiedByGrantedPolicies(string requiredPolicy, IEnumerable<string> grantedPolicies)
    {
        if (string.IsNullOrEmpty(requiredPolicy) || grantedPolicies is null)
            return false;

        foreach (var policy in grantedPolicies)
        {
            if (string.IsNullOrEmpty(policy))
                continue;
            if (policy == requiredPolicy || HasChildPolicy(policy, requiredPolicy, new HashSet<string>(StringComparer.Ordinal)))
                return true;
        }
        return false;
    }

    // Recursively walks the child policy tree to check if target is reachable from policy.
    // _childMap stores direct children only; this is very fast for shallow trees
    // if necessary down the road we could precompute transitive resolving like it's done in core's PolicyMap.
    private bool HasChildPolicy(string policy, string target, HashSet<string> visited)
    {
        if (!_childMap.TryGetValue(policy, out var children) || !visited.Add(policy))
            return false;
        return children.Contains(target) || children.Any(c => HasChildPolicy(c, target, visited));
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
            return $"⚠️ [Uninstalled Plugin] {_culture.TextInfo.ToTitleCase(permissionName)}";
        }

        return Policies.DisplayName(policy);
    }
}
