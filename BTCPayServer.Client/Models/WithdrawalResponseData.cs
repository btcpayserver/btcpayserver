using System;
using System.Collections.Generic;
using Newtonsoft.Json;
using Newtonsoft.Json.Converters;

namespace BTCPayServer.Client.Models;

public class WithdrawalResponseData : WithdrawalBaseResponseData
{

    [JsonConverter(typeof(StringEnumConverter))]
    public WithdrawalStatus Status { get; }

    public string WithdrawalId { get; }
    public DateTimeOffset CreatedTime { get; }

    public string TransactionId { get; }

    public string TargetAddress { get; }

    public WithdrawalResponseData(string paymentMethod, string asset, List<LedgerEntryData> ledgerEntries, string withdrawalId, string accountId,
        string custodianCode, WithdrawalStatus status, DateTimeOffset createdTime, string targetAddress, string transactionId) : base(paymentMethod, asset, ledgerEntries, accountId,
        custodianCode)
    {
        WithdrawalId = withdrawalId;
        TargetAddress = targetAddress;
        TransactionId = transactionId;
        Status = status;
        CreatedTime = createdTime;
    }


    public enum WithdrawalStatus
    {
        Unknown = 0,
        Queued = 1,
        Complete = 2,
        Failed = 3
    }
}
