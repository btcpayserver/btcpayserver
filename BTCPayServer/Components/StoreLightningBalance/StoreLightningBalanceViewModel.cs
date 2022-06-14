using System.Collections.Generic;
using BTCPayServer.Data;
using BTCPayServer.Lightning;
using BTCPayServer.Models;
using BTCPayServer.Models.StoreViewModels;

namespace BTCPayServer.Components.StoreLightningBalance;

public class StoreLightningBalanceViewModel
{
    public string CryptoCode { get; set; }
    public StoreData Store { get; set; }
    public WalletId WalletId { get; set; }
    public LightMoney TotalOnchain { get; set; }
    public LightMoney TotalOffchain { get; set; }
    public LightningNodeBalance Balance { get; set; }
    public string ProblemDescription { get; set; }
}
