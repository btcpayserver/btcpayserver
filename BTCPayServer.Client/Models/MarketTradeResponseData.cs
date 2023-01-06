using System.Collections.Generic;

namespace BTCPayServer.Client.Models;

public class MarketTradeResponseData
{
    public string FromAsset { get; }
    public string ToAsset { get; }
    /**
     * The ledger entries that show the balances that were affected by the trade.
     */
    public List<LedgerEntryData> LedgerEntries { get; }
    /**
     * The unique ID of the trade that was executed.
     */
    public string TradeId { get; }

    public string AccountId { get; }

    public string CustodianCode { get; }

    public MarketTradeResponseData(string fromAsset, string toAsset, List<LedgerEntryData> ledgerEntries, string tradeId, string accountId, string custodianCode)
    {
        FromAsset = fromAsset;
        ToAsset = toAsset;
        LedgerEntries = ledgerEntries;
        TradeId = tradeId;
        AccountId = accountId;
        CustodianCode = custodianCode;
    }
}
