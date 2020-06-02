using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitGroestlcoin(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("GRS");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Groestlcoin",
                BlockExplorerLink = nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet
                    ? "https://chainz.cryptoid.info/grs/tx.dws?{0}.htm"
                    : "https://chainz.cryptoid.info/grs-test/tx.dws?{0}.htm",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "groestlcoin",
                DefaultRateRules = new[] {"GRS_X = GRS_BTC * BTC_X", "GRS_BTC = bittrex(GRS_BTC)"},
                CryptoImagePath = "imlegacy/groestlcoin.png",
                LightningImagePath = "imlegacy/groestlcoin-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet
                    ? new KeyPath("17'")
                    : new KeyPath("1'"),
                SupportRBF = true,
                SupportPayJoin = true
            };
        }
    }
}
