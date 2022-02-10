namespace BTCPayServer.Models.StoreViewModels;

public class StoreDashboardViewModel
{
    public string StoreId { get; set; }
    public string StoreName { get; set; }
    public bool WalletEnabled { get; set; }
    public bool LightningEnabled { get; set; }
}
