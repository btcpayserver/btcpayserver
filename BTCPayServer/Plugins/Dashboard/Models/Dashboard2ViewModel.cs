#nullable enable
namespace BTCPayServer.Plugins.Dashboard.Models;

public class Dashboard2ViewModel
{
    public string? DashboardId { get; set; }
    public bool IsSetUp { get; set; }
    public bool WalletEnabled { get; set; }
    public bool LightningEnabled { get; set; }
    public bool LightningSupported { get; set; }
    public string CryptoCode { get; set; } = "BTC";
}
