using NBitcoin;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitChaincoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("CHC");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Chaincoin",
                BlockExplorerLink = NetworkType == NetworkType.Mainnet
                    ? "https://explorer.chaincoin.org/Explorer/Transaction/{0}"
                    : "https://test.explorer.chaincoin.org/Explorer/Transaction/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "chaincoin",
                DefaultRateRules = new[]
                    {
                        "CHC_X = CHC_BTC * BTC_X",
                        "CHC_BTC = txbit(CHC_X)"
                    },
                CryptoImagePath = "imlegacy/chaincoin.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                //https://github.com/satoshilabs/slips/blob/master/slip-0044.md
                CoinType = NetworkType == NetworkType.Mainnet ? new KeyPath("711'")
                    : new KeyPath("1'")
            });
        }
    }
}

