using System;
using System.Collections.Generic;
using BTCPayServer.Models.WalletViewModels;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactionViewModel
{
    public string Id { get; set; }
    public string Currency { get; set; }
    public string Balance { get; set; }
    public bool Positive { get; set; }
    public bool IsConfirmed { get; set; }
    public string Link { get; set; }
    public DateTimeOffset Timestamp { get; set; }
    public IEnumerable<TransactionTagModel> Labels { get; set; }
}
