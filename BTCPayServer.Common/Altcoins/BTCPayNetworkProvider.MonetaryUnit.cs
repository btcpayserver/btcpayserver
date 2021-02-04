using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitMonetaryUnit()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("MUE");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "MonetaryUnit",
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://explorer.monetaryunit.org/#/MUE/mainnet/tx/{0}" : "https://explorer.monetaryunit.org/#/MUE/mainnet/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "monetaryunit",
                DefaultRateRules = new[]
                {
                                "MUE_X = MUE_BTC * BTC_X",
                                "MUE_BTC = bittrex(MUE_BTC)"
                },
                CryptoImagePath = "imlegacy/monetaryunit.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("31'") : new KeyPath("1'")
            });
        }
    }
}
