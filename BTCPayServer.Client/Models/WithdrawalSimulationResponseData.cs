using System.Collections.Generic;

namespace BTCPayServer.Client.Models;

public class WithdrawalSimulationResponseData : WithdrawalBaseResponseData
{
    public decimal? MinQty { get; set; }

    public decimal? MaxQty { get; set; }

    public WithdrawalSimulationResponseData(string paymentMethod, string asset, string accountId,
        string custodianCode, List<LedgerEntryData> ledgerEntries, decimal? minQty, decimal? maxQty) : base(paymentMethod,
        asset, ledgerEntries, accountId, custodianCode)
    {
        MinQty = minQty;
        MaxQty = maxQty;
    }
}
