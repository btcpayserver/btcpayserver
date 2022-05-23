namespace BTCPayServer.Client.Models;

public class TradeQuoteResponseData
{
    public decimal Bid { get; }
    public decimal Ask { get; }
    public string ToAsset { get; }
    public string FromAsset { get; }

    public TradeQuoteResponseData(string fromAsset, string toAsset, decimal bid, decimal ask)
    {
        FromAsset = fromAsset;
        ToAsset = toAsset;
        Bid = bid;
        Ask = ask;
    }
}
