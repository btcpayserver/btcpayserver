using System.Collections;
using BTCPayServer.Data;

namespace BTCPayServer.Components.StoreNumbers;

public class StoreNumbersViewModel
{
    public StoreData Store { get; set; }
    public WalletId WalletId { get; set; }
    public int PayoutsPending { get; set; }
    public int Transactions { get; set; }
    public int RefundsIssued { get; set; }
    public int TransactionDays { get; set; }
}
