using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitGroestlcoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("GRS");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Groestlcoin",
                BlockExplorerLink = NetworkType == ChainName.Mainnet
                    ? "https://chainz.cryptoid.info/grs/tx.dws?{0}.htm"
                    : "https://chainz.cryptoid.info/grs-test/tx.dws?{0}.htm",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "groestlcoin",
                DefaultRateRules = new[]
                {
                    "GRS_X = GRS_BTC * BTC_X",
                    "GRS_BTC = bittrex(GRS_BTC)"
                },
                CryptoImagePath = "imlegacy/groestlcoin.png",
                LightningImagePath = "imlegacy/groestlcoin-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("17'") : new KeyPath("1'"),
                SupportRBF = true,
                SupportPayJoin = true,
                VaultSupported = true
            });
        }
    }
}
