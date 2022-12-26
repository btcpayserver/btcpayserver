using System.Collections.Generic;

namespace BTCPayServer.Client.Models;

public abstract class WithdrawalBaseResponseData
{
    public string Asset { get; }
    public string PaymentMethod { get; }
    public List<LedgerEntryData> LedgerEntries { get; }
    public string AccountId { get; }
    public string CustodianCode { get; }

    public WithdrawalBaseResponseData(string paymentMethod, string asset, List<LedgerEntryData> ledgerEntries, string accountId,
        string custodianCode)
    {
        PaymentMethod = paymentMethod;
        Asset = asset;
        LedgerEntries = ledgerEntries;
        AccountId = accountId;
        CustodianCode = custodianCode;
    }
}
