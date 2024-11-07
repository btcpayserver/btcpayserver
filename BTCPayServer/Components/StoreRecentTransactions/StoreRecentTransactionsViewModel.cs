using System.Collections.Generic;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactionsViewModel
{
    public string StoreId { get; set; }
    public IList<StoreRecentTransactionViewModel> Transactions { get; set; } = new List<StoreRecentTransactionViewModel>();
    public WalletId WalletId { get; set; }
    public bool InitialRendering { get; set; }
    public string CryptoCode { get; set; }
}
