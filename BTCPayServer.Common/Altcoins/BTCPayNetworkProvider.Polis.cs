using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitPolis(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("POLIS");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Polis",
                BlockExplorerLink = nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet ? "https://blockbook.polispay.org/tx/{0}" : "https://blockbook.polispay.org/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "polis",
                DefaultRateRules = new[]
                {
                                "POLIS_X = POLIS_BTC * BTC_X",
                                "POLIS_BTC = polispay(POLIS_BTC)"
                },
                CryptoImagePath = "imlegacy/polis.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType == NetworkType.Mainnet ? new KeyPath("1997'") : new KeyPath("1'")
            };
        }
    }
}
