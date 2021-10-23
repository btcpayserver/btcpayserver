using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class BTCPayNetworkProvider
    {
        public void InitViacoin()
        {
            var nbxplorerNetwork = NBXplorerNetworkProvider.GetFromCryptoCode("VIA");
            Add(new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Viacoin",
                BlockExplorerLink = NetworkType == ChainName.Mainnet ? "https://explorer.viacoin.org/tx/{0}" : "https://explorer.viacoin.org/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                DefaultRateRules = new[]
                {
                                "VIA_X = VIA_BTC * BTC_X",
                                "VIA_BTC = bittrex(VIA_BTC)"
                },
                CryptoImagePath = "imlegacy/viacoin.png",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(NetworkType),
                CoinType = NetworkType == ChainName.Mainnet ? new KeyPath("14'") : new KeyPath("1'")
            });
        }
    }
}
