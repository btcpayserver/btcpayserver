using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactionsViewModel
{
    public StoreData Store { get; set; }
    public IEnumerable<StoreRecentTransactionViewModel> Transactions { get; set; }
    public WalletId WalletId { get; set; }
}
