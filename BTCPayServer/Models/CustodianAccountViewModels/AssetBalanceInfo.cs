using System.Collections.Generic;
using BTCPayServer.Client.Models;

namespace BTCPayServer.Models.CustodianAccountViewModels;

public class AssetBalanceInfo
{

    public string Asset { get; set; }
    public decimal? Bid { get; set; }
    public decimal? Ask { get; set; }
    public decimal Qty { get; set; }
    public string FormattedQty { get; set; }
    public string FormattedFiatValue { get; set; }
    public decimal? FiatValue { get; set; }
    public Dictionary<string, AssetPairData> TradableAssetPairs { get; set; }

    public List<string> WithdrawablePaymentMethods { get; set; } = new();
    public string FormattedBid { get; set; }
    public string FormattedAsk { get; set; }
}
