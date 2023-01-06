namespace BTCPayServer.Abstractions.Custodians.Client;

public class AssetQuoteResult
{
    public string FromAsset { get; set; }
    public string ToAsset { get; set; }
    public decimal Bid { get; set; }
    public decimal Ask { get; set; }

    public AssetQuoteResult() { }

    public AssetQuoteResult(string fromAsset, string toAsset, decimal bid, decimal ask)
    {
        FromAsset = fromAsset;
        ToAsset = toAsset;
        Bid = bid;
        Ask = ask;
    }
}
