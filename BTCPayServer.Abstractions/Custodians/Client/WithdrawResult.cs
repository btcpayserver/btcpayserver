using System;
using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Abstractions.Custodians.Client;

public class WithdrawResult
{
    public string PaymentMethod { get; }
    public string Asset { get; set; }
    public List<LedgerEntryData> LedgerEntries { get; }
    public string WithdrawalId { get; }
    public WithdrawalResponseData.WithdrawalStatus Status { get; }
    public DateTimeOffset CreatedTime { get; }
    public string TargetAddress { get; }
    public string TransactionId { get; }

    public WithdrawResult(string paymentMethod, string asset, List<LedgerEntryData> ledgerEntries, string withdrawalId, WithdrawalResponseData.WithdrawalStatus status, DateTimeOffset createdTime, string targetAddress, string transactionId)
    {
        PaymentMethod = paymentMethod;
        Asset = asset;
        LedgerEntries = ledgerEntries;
        WithdrawalId = withdrawalId;
        CreatedTime = createdTime;
        Status = status;
        TargetAddress = targetAddress;
        TransactionId = transactionId;
    }
}
