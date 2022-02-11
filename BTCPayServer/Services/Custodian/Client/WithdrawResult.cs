using System.Collections.Generic;
using BTCPayServer.Data;

namespace BTCPayServer.Services.Custodian.Client;

public class WithdrawResult
{
    public string Asset { get; }
    public List<LedgerEntryData> LedgerEntries { get; }
    public string WithdrawalId { get; }
    public WithdrawResultData.WithdrawalStatus Status { get; }
    public string TargetAddress { get; }
    public string TransactionId { get; }

    public WithdrawResult(string asset, List<LedgerEntryData> ledgerEntries, string withdrawalId, WithdrawResultData.WithdrawalStatus status, string targetAddress, string transactionId)
    {
        this.Asset = asset;
        this.LedgerEntries = ledgerEntries;
        this.WithdrawalId = withdrawalId;
        this.Status = status;
        this.TargetAddress = targetAddress;
        this.TransactionId = transactionId;
    }
}


