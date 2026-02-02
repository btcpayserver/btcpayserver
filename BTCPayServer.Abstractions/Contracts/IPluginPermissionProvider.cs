using System.Collections.Generic;

namespace BTCPayServer.Abstractions.Contracts;

/// <summary>
/// Interface for plugins to provide custom permissions that can be assigned to roles
/// </summary>
public interface IPluginPermissionProvider
{
    /// <summary>
    /// Returns the list of permissions provided by this plugin
    /// </summary>
    IEnumerable<PluginPermission> GetPermissions();
}

/// <summary>
/// Represents a permission provided by a plugin
/// </summary>
public class PluginPermission
{
    /// <summary>
    /// The policy string (e.g., "btcpay.plugin.vendorpay.manage")
    /// Must start with "btcpay.plugin."
    /// </summary>
    public string Policy { get; set; }

    /// <summary>
    /// Display name shown in the UI (e.g., "Vendor Pay: Manage")
    /// </summary>
    public string DisplayName { get; set; }

    /// <summary>
    /// User-friendly description of what this permission allows
    /// </summary>
    public string Description { get; set; }

    /// <summary>
    /// The identifier of the plugin that provides this permission
    /// </summary>
    public string PluginIdentifier { get; set; }

    /// <summary>
    /// Child policies that are granted when this permission is granted
    /// Used for hierarchical permissions
    /// </summary>
    public List<string> ChildPolicies { get; set; } = new();

    /// <summary>
    /// The scope of this permission (Store, Server, or User)
    /// </summary>
    public PermissionScope Scope { get; set; } = PermissionScope.Store;
}

/// <summary>
/// Defines the scope of a permission
/// </summary>
public enum PermissionScope
{
    /// <summary>
    /// Permission applies at the store level
    /// </summary>
    Store,

    /// <summary>
    /// Permission applies at the server level
    /// </summary>
    Server,

    /// <summary>
    /// Permission applies at the user level
    /// </summary>
    User
}
