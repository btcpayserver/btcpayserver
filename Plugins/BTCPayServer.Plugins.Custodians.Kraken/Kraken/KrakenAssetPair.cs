using BTCPayServer.Client.Models;

namespace BTCPayServer.Plugins.Custodians.Kraken.Kraken;

public class KrakenAssetPair: AssetPairData
{
    public string PairCode { get; }
    
    public KrakenAssetPair(string AssetBought, string AssetSold, string PairCode) : base(AssetBought, AssetSold)
    {
        this.PairCode = PairCode;
    }
}
