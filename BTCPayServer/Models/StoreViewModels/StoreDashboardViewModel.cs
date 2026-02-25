using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Models;

namespace BTCPayServer.Models.StoreViewModels;

public class StoreDashboardViewModel
{
    public string StoreId { get; set; }
    public string CryptoCode { get; set; }
    public string StoreName { get; set; }
    public bool WalletEnabled { get; set; }
    public bool LightningEnabled { get; set; }
    public bool LightningSupported { get; set; }
    public bool IsSetUp { get; set; }
    public List<AppData> Apps { get; set; } = new();
    public List<MultisigInProgressViewModel> MultisigInProgress { get; set; } = new();
    public BTCPayNetworkBase Network { get; set; }
}
