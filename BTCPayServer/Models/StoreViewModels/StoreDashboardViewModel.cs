using System.Collections.Generic;
using System.ComponentModel.DataAnnotations;
using BTCPayServer.Data;

namespace BTCPayServer.Models.StoreViewModels;

public class StoreDashboardViewModel
{
    public string StoreId { get; set; }
    
    [Display(Name = "Crypto Code")]
    public string CryptoCode { get; set; }
    
    [Display(Name = "Store Name")]
    public string StoreName { get; set; }
    
    [Display(Name = "Wallet Enabled")]
    public bool WalletEnabled { get; set; }
    
    [Display(Name = "Lightning Enabled")]
    public bool LightningEnabled { get; set; }
    
    [Display(Name = "Lightning Supported")]
    public bool LightningSupported { get; set; }
    
    [Display(Name = "Is Set Up")]
    public bool IsSetUp { get; set; }
    
    public List<string> EnabledWalletCryptos { get; set; } = new();
    public List<AppData> Apps { get; set; } = new();
    public BTCPayNetworkBase Network { get; set; }
}
