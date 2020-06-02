using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitUfo(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("UFO");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Ufo",
                BlockExplorerLink = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? "https://chainz.cryptoid.info/ufo/tx.dws?{0}" : "https://chainz.cryptoid.info/ufo/tx.dws?{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "ufo",
                DefaultRateRules = new[] 
                {
                                "UFO_X = UFO_BTC * BTC_X",
                                "UFO_BTC = coinexchange(UFO_BTC)"
                },
                CryptoImagePath = "imlegacy/ufo.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? new KeyPath("202'") : new KeyPath("1'")
            };
        }
    }
}
