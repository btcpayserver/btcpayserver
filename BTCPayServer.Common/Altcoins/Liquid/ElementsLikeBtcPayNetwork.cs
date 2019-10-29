using NBitcoin;

namespace BTCPayServer
{
    public class ElementsAssetBTCPayNetwork:BTCPayNetwork
    {
        public string NetworkCryptoCode { get; set; }
        public uint256 AssetId { get; set; }
    }
}
