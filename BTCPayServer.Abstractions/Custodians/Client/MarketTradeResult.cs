using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Abstractions.Custodians.Client;

/**
 * The result of a market trade. Used as a return type for custodians implementing ICanTrade
 */
public class MarketTradeResult
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

    public MarketTradeResult(string fromAsset, string toAsset, List<LedgerEntryData> ledgerEntries, string tradeId)
    {
        this.FromAsset = fromAsset;
        this.ToAsset = toAsset;
        this.LedgerEntries = ledgerEntries;
        this.TradeId = tradeId;
    }
}
