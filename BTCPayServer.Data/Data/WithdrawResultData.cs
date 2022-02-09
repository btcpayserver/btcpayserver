using System.Collections.Generic;

namespace BTCPayServer.Data;

public class WithdrawResultData
{
    public string Asset { get; }
    public List<LedgerEntryData> LedgerEntries { get; }
    public string WithdrawalId { get; }
    public string AccountId { get; }
    public string CustodianCode { get; }

    public WithdrawResultData(string Asset, List<LedgerEntryData> LedgerEntries, string WithdrawalId, string AccountId,
        string CustodianCode)
    {
        this.Asset = Asset;
        this.LedgerEntries = LedgerEntries;
        this.WithdrawalId = WithdrawalId;
        this.AccountId = AccountId;
        this.CustodianCode = CustodianCode;
    }
    
}
