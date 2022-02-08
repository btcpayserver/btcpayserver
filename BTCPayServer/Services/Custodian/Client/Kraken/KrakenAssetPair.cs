using BTCPayServer.Client.Models;

namespace BTCPayServer.Services.Custodian.Client.Kraken;

public class KrakenAssetPair: AssetPairData
{
    public string PairCode { get; }
    
    public KrakenAssetPair(string AssetBought, string AssetSold, string PairCode) : base(AssetBought, AssetSold)
    {
        this.PairCode = PairCode;
    }
}
