using System;

namespace BTCPayServer.Plugins.Subscriptions;

public class BalanceTransaction
{
    public long SubscriberId { get; }
    public decimal Credit { get; }
    public decimal Debit { get; }
    public string Description { get; }
    public string Currency { get; set; }
    public decimal Diff { get; }

    public BalanceTransaction(long subscriberId, string currency, decimal credit, decimal debit, string description)
    {
        if (credit < 0)
            throw new ArgumentOutOfRangeException(nameof(credit), "Credit must be positive.");

        if (debit < 0)
            throw new ArgumentOutOfRangeException(nameof(debit), "Debit must be positive.");

        SubscriberId = subscriberId;
        Credit = credit;
        Debit = debit;
        Description = description;
        Diff = Credit - Debit;
        Currency = currency;
    }
}
