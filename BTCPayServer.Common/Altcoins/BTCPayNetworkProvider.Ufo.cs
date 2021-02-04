using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitUfo()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("UFO");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Ufo",
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://chainz.cryptoid.info/ufo/tx.dws?{0}" : "https://chainz.cryptoid.info/ufo/tx.dws?{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "ufo",
                DefaultRateRules = new[]
                {
                                "UFO_X = UFO_BTC * BTC_X",
                                "UFO_BTC = coinexchange(UFO_BTC)"
                },
                CryptoImagePath = "imlegacy/ufo.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("202'") : new KeyPath("1'")
            });
        }
    }
}
