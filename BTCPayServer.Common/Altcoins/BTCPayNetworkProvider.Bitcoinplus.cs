using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitBitcoinplus(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("XBC");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Bitcoinplus",
                BlockExplorerLink =
                    nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet
                        ? "https://chainz.cryptoid.info/xbc/tx.dws?{0}"
                        : "https://chainz.cryptoid.info/xbc/tx.dws?{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcoinplus",
                DefaultRateRules = new[] {"XBC_X = XBC_BTC * BTC_X", "XBC_BTC = cryptopia(XBC_BTC)"},
                CryptoImagePath = "imlegacy/bitcoinplus.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet
                    ? new KeyPath("65'")
                    : new KeyPath("1'")
            };
        }
    }
}
