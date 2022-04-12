using System;

namespace BTCPayServer.Components.StoreRecentTransactions;

public class StoreRecentTransactionViewModel
{
    public string Id { get; set; }
    public string Balance { get; set; }
    public bool Positive { get; set; }
    public bool IsConfirmed { get; set; }
    public string Link { get; set; }
    public DateTimeOffset Timestamp { get; set; }
}
