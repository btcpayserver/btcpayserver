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
                UriScheme = "chc",
                DefaultRateRules = new[]
                    {
                        "CVC_X = CVC_BTC * BTC_X",
                        "CVC_BTC = bittrex(CVC_BTC)"
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
