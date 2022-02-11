using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Services.Custodian.Client;

public class WithdrawResult
{
    public string Asset { get; }
    public List<LedgerEntryData> LedgerEntries { get; }
    public string WithdrawalId { get; }
    public string TargetAddress { get; }

    public WithdrawResult(string Asset, List<LedgerEntryData> LedgerEntries, string WithdrawalId, string targetAddress)
    {
        this.Asset = Asset;
        this.LedgerEntries = LedgerEntries;
        this.WithdrawalId = WithdrawalId;
        this.TargetAddress = targetAddress;
    }
}
