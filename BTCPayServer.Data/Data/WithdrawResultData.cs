using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Data;

public class WithdrawResultData
{
    public string Asset { get; }
    public List<LedgerEntryData> LedgerEntries { get; }
    public string WithdrawalId { get; }
    public string AccountId { get; }
    public string CustodianCode { get; }

    [JsonConverter(typeof(StringEnumConverter))]
    public WithdrawalStatus Status { get; }

    public string TransactionId { get; }

    public string TargetAddress { get; }

    public WithdrawResultData(string asset, List<LedgerEntryData> ledgerEntries, string withdrawalId, string accountId,
        string custodianCode, WithdrawalStatus status, string targetAddress, string transactionId)
    {
        this.Asset = asset;
        this.LedgerEntries = ledgerEntries;
        this.WithdrawalId = withdrawalId;
        this.AccountId = accountId;
        this.CustodianCode = custodianCode;
        this.TargetAddress = targetAddress;
        this.TransactionId = transactionId;
        this.Status = status;
    }

    public enum WithdrawalStatus
    {
        Unknown = 0,
        Queued = 1,
        Complete = 2,
        Failed = 3
    }
}
