using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitBPlus()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("XBC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "BPlus",
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://chainz.cryptoid.info/xbc/tx.dws?{0}" : "https://chainz.cryptoid.info/xbc/tx.dws?{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bplus-fix-it",
                DefaultRateRules = new[]
                {
                    "XBC_X = XBC_BTC * BTC_X",
                    "XBC_BTC = cryptopia(XBC_BTC)"
                },
                CryptoImagePath = "imlegacy/xbc.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("65'") : new KeyPath("1'")
            });
        }
    }
}
