using NBitcoin;
using NBXplorer;

namespace BTCPayServer
{
    public partial class AltcoinBTCPayNetworkProvider
    {
        public BTCPayNetworkBase InitBitcore(NBXplorerNetworkProvider nbXplorerNetworkProvider)
        {
            var nbxplorerNetwork = nbXplorerNetworkProvider.GetFromCryptoCode("BTX");
            return new BTCPayNetwork()
            {
                CryptoCode = nbxplorerNetwork.CryptoCode,
                DisplayName = "Bitcore",
                BlockExplorerLink = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? "https://insight.bitcore.cc/tx/{0}" : "https://insight.bitcore.cc/tx/{0}",
                NBXplorerNetwork = nbxplorerNetwork,
                UriScheme = "bitcore",
                DefaultRateRules = new[]
                {
                                "BTX_X = BTX_BTC * BTC_X",
                                "BTX_BTC = hitbtc(BTX_BTC)"
                },
                CryptoImagePath = "imlegacy/bitcore.svg",
                LightningImagePath = "imlegacy/bitcore-lightning.svg",
                DefaultSettings = BTCPayDefaultSettings.GetDefaultSettings(nbXplorerNetworkProvider.NetworkType),
                CoinType = nbXplorerNetworkProvider.NetworkType ==  NetworkType.Mainnet ? new KeyPath("160'") : new KeyPath("1'")
            };
        }
    }
}
