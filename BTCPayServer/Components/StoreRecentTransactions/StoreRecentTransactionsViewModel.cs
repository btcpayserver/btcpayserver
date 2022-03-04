using System.Collections;
using BTCPayServer.Data;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactionsViewModel
{
    public StoreData Store { get; set; }
    public IEnumerable Entries { get; set; }
    public WalletId WalletId { get; set; }
}
