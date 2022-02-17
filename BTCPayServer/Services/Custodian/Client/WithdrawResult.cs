using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Services.Custodian.Client;

public class WithdrawResult
{
    public string PaymentMethod { get; }
    public string Asset { get; set; }
    public List<LedgerEntryData> LedgerEntries { get; }
    public string WithdrawalId { get; }
    public WithdrawResultData.WithdrawalStatus Status { get; }
    public string TargetAddress { get; }
    public string TransactionId { get; }

    public WithdrawResult(string paymentMethod, string asset, List<LedgerEntryData> ledgerEntries, string withdrawalId, WithdrawResultData.WithdrawalStatus status, string targetAddress, string transactionId)
    {
        this.PaymentMethod = paymentMethod;
        this.Asset = asset;
        this.LedgerEntries = ledgerEntries;
        this.WithdrawalId = withdrawalId;
        this.Status = status;
        this.TargetAddress = targetAddress;
        this.TransactionId = transactionId;
    }
}
