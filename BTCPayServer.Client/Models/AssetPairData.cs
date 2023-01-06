using Newtonsoft.Json;

namespace BTCPayServer.Client.Models;

[JsonObject(MemberSerialization.OptIn)]
public class AssetPairData
{
    public AssetPairData()
    {
    }

    public AssetPairData(string assetBought, string assetSold, decimal minimumTradeQty)
    {
        AssetBought = assetBought;
        AssetSold = assetSold;
        MinimumTradeQty = minimumTradeQty;
    }

    [JsonProperty]
    public string AssetBought { set; get; }

    [JsonProperty]
    public string AssetSold { set; get; }

    [JsonProperty]
    public decimal MinimumTradeQty { set; get; }


    public override string ToString()
    {
        return AssetBought + "/" + AssetSold;
    }
}
