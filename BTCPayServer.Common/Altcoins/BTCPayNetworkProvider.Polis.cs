using NBitcoin;

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
                BlockExplorerLink = NetworkType == NetworkType.Mainnet ? "https://insight.polispay.org/tx/{0}" : "https://insight.polispay.org/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "polis",
                DefaultRateRules = new[]
                {
                                "POLIS_X = POLIS_BTC * BTC_X",
                                "POLIS_BTC = cryptopia(POLIS_BTC)"
                },
                CryptoImagePath = "imlegacy/polis.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("1997'") : new KeyPath("1'")
            });
        }
    }
}
