namespace BTCPayServer.Client.Models;

public class AssetPairData
{

    public AssetPairData()
    {
    }
    
    public AssetPairData(string AssetBought, string AssetSold)
    {
        this.AssetBought = AssetBought;
        this.AssetSold = AssetSold;
    }
    
    public string AssetBought { set; get; }
    public string AssetSold { set; get; }

    public string ToString()
    {
        return AssetBought + "/" + AssetSold;
    }
}
