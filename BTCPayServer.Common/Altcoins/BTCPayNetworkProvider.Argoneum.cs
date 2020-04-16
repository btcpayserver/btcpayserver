using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitArgoneum()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("AGM");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Argoneum",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet
                    ? "https://chainz.cryptoid.info/agm/tx.dws?{0}"
                    : "https://chainz.cryptoid.info/agm-test/tx.dws?{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "argoneum",
                DefaultRateRules = new[]
                    {
                        "AGM_X = AGM_BTC * BTC_X",
                        "AGM_BTC = argoneum(AGM_BTC)"
                    },
                CryptoImagePath = "imlegacy/argoneum.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("421'")
                    : new KeyPath("1'")
            });
        }
    }
}
