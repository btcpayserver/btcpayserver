namespace BTCPayServer.Abstractions.Custodians;

public class AssetQuoteResult
{
    public string FromAsset { get; }
    
    public string ToAsset { get; }
    public decimal Bid { get; }
    public decimal Ask { get; }

    public AssetQuoteResult(string fromAsset, string toAsset,decimal bid, decimal ask)
    {
        this.FromAsset = fromAsset;
        this.ToAsset = toAsset;
        this.Bid = bid;
        this.Ask = ask;
    }
}
