using System.Collections.Generic;
using BTCPayServer.Client.Models;
using BTCPayServer.JsonConverters;

namespace BTCPayServer.Abstractions.Custodians.Client;

public class SimulateWithdrawalResult
{
    public string PaymentMethod { get; }
    public string Asset { get; }
    public decimal MinQty { get; }
    public decimal MaxQty { get; }

    public List<LedgerEntryData> LedgerEntries { get; }

    // Fee can be NULL if unknown.
    public decimal? Fee { get; }

    public SimulateWithdrawalResult(string paymentMethod, string asset, List<LedgerEntryData> ledgerEntries,
        decimal minQty, decimal maxQty)
    {
        PaymentMethod = paymentMethod;
        Asset = asset;
        LedgerEntries = ledgerEntries;
        MinQty = minQty;
        MaxQty = maxQty;
    }
}
