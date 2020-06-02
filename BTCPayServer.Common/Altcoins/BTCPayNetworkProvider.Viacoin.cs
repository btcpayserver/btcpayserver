using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitViacoin(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("VIA");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Viacoin",
                BlockExplorerLink =
                    nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet
                        ? "https://explorer.viacoin.org/tx/{0}"
                        : "https://explorer.viacoin.org/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "viacoin",
                DefaultRateRules = new[] {"VIA_X = VIA_BTC * BTC_X", "VIA_BTC = bittrex(VIA_BTC)"},
                CryptoImagePath = "imlegacy/viacoin.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet
                    ? new KeyPath("14'")
                    : new KeyPath("1'")
            };
        }
    }
}
