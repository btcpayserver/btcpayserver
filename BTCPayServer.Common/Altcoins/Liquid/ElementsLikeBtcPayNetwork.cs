using System.Collections.Generic;
using System.Linq;
using NBitcoin;
using NBitcoin.Altcoins.Elements;

namespace BTCPayServer
{
    public class ElementsBTCPayNetwork:BTCPayNetwork
    {
        public string NetworkCryptoCode { get; set; }
        public uint256 AssetId { get; set; }
        public int Divisibility { get; set; } = 8;
        
        public override bool WalletSupported { get; set; } = false;
        public override IEnumerable<Coin> GetValidCoinsForNetwork(IEnumerable<Coin> coins, Script scriptPubKey)
        {
            return base.GetValidCoinsForNetwork(coins, scriptPubKey)
                .Where(coin =>
                {
                    if (coin.TxOut is ElementsTxOut elementsTxOut)
                    {
                        return (AssetId == null && elementsTxOut.IsPeggedAsset.GetValueOrDefault(false)) ||
                               (AssetId != null && elementsTxOut.Asset.AssetId == AssetId);
                    }
                    return false;
                });
        }
    }
}
