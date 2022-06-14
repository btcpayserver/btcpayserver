namespace BTCPayServer.Client.Models;

public class AssetPairData
{
    public AssetPairData()
    {
    }
    
    public AssetPairData(string assetBought, string assetSold)
    {
        AssetBought = assetBought;
        AssetSold = assetSold;
    }
    
    public string AssetBought { set; get; }
    public string AssetSold { set; get; }

    public override string ToString()
    {
        return AssetBought + "/" + AssetSold;
    }
}
