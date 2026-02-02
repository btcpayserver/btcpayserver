using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;
using BTCPayServer.Abstractions.Contracts;

namespace BTCPayServer.Services;

public interface IPluginPermissionRegistry
{
    /// <summary>
    /// Register permissions from a plugin
    /// </summary>
    void RegisterPermissions(IEnumerable<PluginPermission> permissions);

    /// <summary>
    /// Get all registered plugin permissions
    /// </summary>
    IEnumerable<PluginPermission> GetAllPluginPermissions();

    /// <summary>
    /// Get a specific permission by policy string
    /// </summary>
    PluginPermission GetPermission(string policy);

    /// <summary>
    /// Get the policy map for plugin permissions (for hierarchical permissions)
    /// </summary>
    Dictionary<string, HashSet<string>> GetPluginPolicyMap();

    /// <summary>
    /// Check if a policy is a registered plugin permission
    /// </summary>
    bool IsRegisteredPluginPermission(string policy);
}

public class PluginPermissionRegistry : IPluginPermissionRegistry
{
    private readonly ConcurrentDictionary<string, PluginPermission> _permissions = new();

    public void RegisterPermissions(IEnumerable<PluginPermission> permissions)
    {
        foreach (var permission in permissions)
        {
            if (string.IsNullOrEmpty(permission.Policy))
                throw new ArgumentException("Permission policy cannot be null or empty");

            if (!permission.Policy.StartsWith("btcpay.plugin.", StringComparison.OrdinalIgnoreCase))
                throw new ArgumentException($"Plugin permission policy must start with 'btcpay.plugin.': {permission.Policy}");

            if (string.IsNullOrEmpty(permission.DisplayName))
                throw new ArgumentException($"Permission display name cannot be null or empty for policy: {permission.Policy}");

            if (string.IsNullOrEmpty(permission.PluginIdentifier))
                throw new ArgumentException($"Permission plugin identifier cannot be null or empty for policy: {permission.Policy}");

            _permissions[permission.Policy] = permission;
        }
    }

    public IEnumerable<PluginPermission> GetAllPluginPermissions()
    {
        return _permissions.Values.ToList();
    }

    public PluginPermission GetPermission(string policy)
    {
        return _permissions.TryGetValue(policy, out var permission) ? permission : null;
    }

    public Dictionary<string, HashSet<string>> GetPluginPolicyMap()
    {
        var policyMap = new Dictionary<string, HashSet<string>>();

        foreach (var permission in _permissions.Values)
        {
            if (permission.ChildPolicies != null && permission.ChildPolicies.Any())
            {
                policyMap[permission.Policy] = new HashSet<string>(permission.ChildPolicies);
            }
        }

        return policyMap;
    }

    public bool IsRegisteredPluginPermission(string policy)
    {
        return _permissions.ContainsKey(policy);
    }
}
