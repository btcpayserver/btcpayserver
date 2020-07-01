using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitPolis()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("POLIS");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Polis",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://blockbook.polispay.org/tx/{0}" : "https://blockbook.polispay.org/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "polis",
                DefaultRateRules = new[]
                {
                                "POLIS_X = POLIS_BTC * BTC_X",
                                "POLIS_BTC = polispay(POLIS_BTC)"
                },
                CryptoImagePath = "imlegacy/polis.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("1997'") : new KeyPath("1'")
            });
        }
    }
}
