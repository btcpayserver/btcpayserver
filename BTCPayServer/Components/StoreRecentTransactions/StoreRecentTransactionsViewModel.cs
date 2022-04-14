using System.Collections;
using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactionsViewModel
{
    public StoreData Store { get; set; }
    public IList<StoreRecentTransactionViewModel> Transactions { get; set; } = new List<StoreRecentTransactionViewModel>();
    public WalletId WalletId { get; set; }
}
