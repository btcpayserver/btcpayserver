#nullable enable
namespace BTCPayServer.Plugins.Dashboard;

/// <summary>
/// Explicit dashboard scope for the Blazor circuit. Set by BtcpayDashboard from
/// its server-rendered parameters; read by the store and access-control adapters.
/// Ambient HTTP context is unavailable inside interactive circuits, so the scope
/// travels here instead of relying on IScopeProvider alone.
/// </summary>
public sealed class BtcpayDashboardContext
{
    public string OwnerKey { get; set; } = string.Empty;
    public string? StoreId { get; set; }
}
