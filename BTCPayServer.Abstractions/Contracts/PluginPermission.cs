using System.Collections.Generic;

namespace BTCPayServer.Abstractions.Contracts;

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
    public List<PluginPermission> ChildPolicies { get; set; } = new();
}
